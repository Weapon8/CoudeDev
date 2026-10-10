using System;
using System.Collections.Generic;

namespace CoudeDev
{
    public struct V3 { public double X, Y, Z; public V3(double x,double y,double z){X=x;Y=y;Z=z;}
        public static V3 operator -(V3 a, V3 b)=>new V3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static V3 operator +(V3 a, V3 b)=>new V3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static V3 operator *(V3 a, double s)=>new V3(a.X*s,a.Y*s,a.Z*s);
        public double Norm()=>Math.Sqrt(X*X+Y*Y+Z*Z); }

    public struct V2 { public double X, Y; public V2(double x,double y){X=x;Y=y;}
        public static V2 operator -(V2 a, V2 b)=>new V2(a.X-b.X,a.Y-b.Y);
        public static V2 operator +(V2 a, V2 b)=>new V2(a.X+b.X,a.Y+b.Y);
        public static V2 operator *(V2 a, double s)=>new V2(a.X*s,a.Y*s);
        public double Norm()=>Math.Sqrt(X*X+Y*Y);
        public static double Dot(V2 a, V2 b)=>a.X*b.X+a.Y*b.Y;
        public static double Cross(V2 a, V2 b)=>a.X*b.Y-a.Y*b.X;
        public V2 Unit(){ double n=Norm(); return n<1e-300?new V2(0,0):new V2(X/n,Y/n); } }

    /// <summary>
    /// Sommet de LWPOLYLINE DXF : P = début du tronçon, Bulge = tan(balayage/4) du tronçon
    /// P -> sommet suivant (0 = droite, &gt;0 = arc anti-horaire, &lt;0 = arc horaire).
    /// </summary>
    public struct BVertex { public V2 P; public double Bulge; public BVertex(V2 p, double b){P=p;Bulge=b;} }

    /// <summary>Développé d'une virole : bords femelle/mâle, contour de découpe en arcs, recouvrement.</summary>
    public class Gore
    {
        public string Name = "";
        public string Tag = "";          // FEMELLE / INTER1.. / MALE  (suffixe du fichier DXF)
        public int PartNo = 1;           // P1 = bout femelle ... PN = bout mâle
        public int Qty = 1;
        public V2[] Female = Array.Empty<V2>();   // bord côté femelle (haut), θ = 0..2π
        public V2[] Male   = Array.Empty<V2>();   // bord côté mâle (bas),    θ = 0..2π
        public double SeamRecF = 0;   // recouvrement côté bord femelle (mm)
        public double SeamRecM = 0;   // recouvrement côté bord mâle (mm)
        public int HeelIndex, ThroatIndex;
        public double Width;                         // corde du bord femelle (hors patte)
        public double DevHaut, DevBas;               // développé le long du bord haut / bas, patte COMPRISE (mm)

        // --- Contour machine (calque DECOUPE) ---
        public List<BVertex> Contour = new();        // UN contour fermé : arcs tangents + 4 coins, patte incluse
        public V2[] ContourPts = Array.Empty<V2>();  // le même, discrétisé (aperçu / encombrement)
        public V2[] Flap = Array.Empty<V2>();        // zone de recouvrement (aperçu)
        public V2 SeamF, SeamM;                      // couture d'origine (bord recouvert) = ligne de pose
        public V2 FlapF, FlapM;                      // bout de patte côté femelle / côté mâle
        public int ArcCount, LineCount;
        public double MaxKinkDeg;                    // plus grande cassure de tangente HORS des 4 coins (contrôle)
    }

    /// <summary>Bande 3D assemblée (bague femelle + bague mâle dans le repère du coude).</summary>
    public class Band3D { public V3[] Fem = Array.Empty<V3>(); public V3[] Mal = Array.Empty<V3>(); }

    /// <summary>
    /// Barème atelier FOURREAUX galva/zinc (même tableau que ep-fourreaux.html du hub) :
    /// développé à plat recouvrement compris. Valeur exacte du tableau si le Ø y figure,
    /// sinon π·Ø + joint K (21,0 jusqu'à Ø85 · 26,2 jusqu'à Ø200 · 36,2 au-delà).
    /// </summary>
    public static class BaremeFourreaux
    {
        static readonly (double d, double dev)[] FX = {
            (50,178),(60,209),(70,241),(75,257),(80,272),(85,288),(90,309),(95,325),(100,340),
            (110,372),(115,387),(120,403),(125,419),(130,435),(140,466),(150,497),(160,529),
            (170,560),(180,592),(190,623),(200,655),(230,761),(250,821),(300,978),(350,1136),
            (400,1293),(500,1607),(550,1765) };

        public static double JointK(double d) => d <= 85 ? 21.0 : d <= 200 ? 26.2 : 36.2;
        /// <summary>Recouvrement sur lequel le barème est basé (15 / 20 / 30 mm).</summary>
        public static double Recouvrement(double d) => d <= 85 ? 15 : d <= 200 ? 20 : 30;

        /// <summary>Développé fourreau (mm) ; exact = true si valeur lue dans le tableau.</summary>
        public static double Dev(double d, out bool exact)
        {
            foreach (var (dd, dev) in FX) if (Math.Abs(dd - d) < 1e-6) { exact = true; return dev; }
            exact = false;
            return Math.PI * d + JointK(d);
        }
        public static double Dev(double d) => Dev(d, out _);
    }

    public static class ElbowGeometry
    {
        public class Params
        {
            public double Diametre = 200;
            /// <summary>true : développé côté femelle (grand côté) = barème fourreaux
            /// (Ø200 -> 655 recouvrement compris). false : circonférence femelle = π·Ø exact.</summary>
            public bool AlignFourreau = true;
            public double Reduction = 25;     // mâle = circonférence - X mm (sur tout le coude)
            /// <summary>Angle RÉEL entre les deux bras, comme on le relève à la fausse équerre :
            /// 180 = tube droit, 135 = coude "à 45°", 90 = équerre.</summary>
            public double AngleReel = 90;
            public int NbSegments = 2;
            public double LongueurBrasExt = 250;   // longueur extrados (extérieur) du bras, mm
            public int NdivDev = 160;
            public int NbTracage = 12;
            public double RecBoutFemelle = 20;   // recouvrement au bout femelle (mm)
            public double RecJoint       = 25;   // recouvrement au joint (mm)
            public double RecBoutMale    = 35;   // recouvrement au bout mâle (mm)
            public double TolArc         = 0.02; // écart maxi contour arcs / courbe exacte (mm)

            /// <summary>Déviation de l'axe = angle dont tourne le tube = 180 - angle réel.</summary>
            public double Deviation => 180.0 - AngleReel;
        }

        static double AlphaRad(Params p) =>
            (p.Deviation * Math.PI / 180.0) / (Math.Max(2, p.NbSegments) - 1) / 2.0;

        // ---- Circonférences réelles du tube (corps roulé, hors recouvrement) ----
        /// <summary>Développé total côté femelle, recouvrement compris (barème fourreaux ou π·Ø + recouv.).</summary>
        public static double DevFemelle(Params p) =>
            p.AlignFourreau ? BaremeFourreaux.Dev(p.Diametre) : Math.PI * p.Diametre + p.RecBoutFemelle;
        /// <summary>Circonférence du corps côté femelle = développé femelle - recouvrement femelle.</summary>
        public static double CircFemelle(Params p) => DevFemelle(p) - p.RecBoutFemelle;
        /// <summary>Circonférence côté mâle = circonférence femelle - réduction X.</summary>
        public static double CircMale(Params p) => CircFemelle(p) - p.Reduction;
        public static double DevMale(Params p) => CircMale(p) + p.RecBoutMale;
        static double RFem(Params p) => CircFemelle(p) / (2 * Math.PI);
        static double RMal(Params p) => CircMale(p) / (2 * Math.PI);

        /// <summary>Contrôle des paramètres. Renvoie null si OK, sinon le message d'erreur.</summary>
        public static string? Validate(Params p)
        {
            if (!(p.Diametre > 0)) return "Ø doit être > 0.";
            if (!(p.AngleReel < 179.9)) return "Angle réel < 180° (180° = tube droit, pas de coude).";
            int N = Math.Max(2, p.NbSegments);
            double alDeg = AlphaRad(p) * 180 / Math.PI;
            if (!(p.AngleReel > 0) || alDeg > 75)
                return $"Onglet de {alDeg:0.#}° par joint : trop fort.\nAugmente l'angle réel ou le nombre d'éléments.";
            if (p.RecBoutFemelle < 0 || p.RecJoint < 0 || p.RecBoutMale < 0) return "Recouvrements >= 0.";
            if (!(CircFemelle(p) > 0.5 * Math.PI * p.Diametre)) return "Recouvrement femelle trop grand pour ce développé.";
            if (p.Reduction < 0 || p.Reduction >= CircFemelle(p) * 0.5) return "Réduction mâle hors limites.";
            if (!(p.LongueurBrasExt > 0)) return "Longueur bras ext. > 0.";
            double Rm = SolveRm(p);
            if (Rm <= RFem(p) + 0.5)
                return $"Gorge nulle ou négative : bras ext. trop court.\nMini ≈ {BrasExtMini(p) + 1:0} mm pour ces réglages.";
            return null;
        }

        /// <summary>Rayon de cintrage à l'axe (calculé depuis la longueur extrados du bras).</summary>
        public static double RayonAxe(Params p) => SolveRm(p);

        /// <summary>Longueur extrados mini : en dessous la gorge du bout femelle passe à 0.</summary>
        public static double BrasExtMini(Params p) => HeelFemelle(p, RFem(p));

        // Longueur extrados (heel, θ=0) du bout femelle pour un Rm donné.
        static double HeelFemelle(Params p, double Rm)
        {
            int N = Math.Max(2, p.NbSegments);
            double al = AlphaRad(p);
            double Lend = Rm * Math.Tan(al), Lint = 2 * Rm * Math.Tan(al);
            double Ltot = 2 * Lend + (N - 2) * Lint;
            if (Ltot < 1e-6) return 0;
            double Rfem = RFem(p);
            double Rmal = RMal(p);
            double Rf = Rfem, r = Rfem + (Rmal - Rfem) * (Lend / Ltot);
            V3 F0 = SolveZ(Rf, r, Lend, 0,  0, true);    // haut droit (femelle)
            V3 M0 = SolveZ(Rf, r, Lend, al, 0, false);   // bas onglet, extrados
            return (F0 - M0).Norm();
        }

        // Trouve Rm pour que l'extrados du bras = LongueurBrasExt (bissection).
        static double SolveRm(Params p)
        {
            double al = AlphaRad(p);
            double lo = 0.5, hi = p.LongueurBrasExt / Math.Max(1e-3, Math.Tan(al)) + p.Diametre + 10;
            for (int it = 0; it < 60; it++)
            {
                double mid = (lo + hi) / 2;
                if (HeelFemelle(p, mid) < p.LongueurBrasExt) lo = mid; else hi = mid;
            }
            return Math.Max(0.5, (lo + hi) / 2);
        }

        struct Spec { public double Rf, r, L; public bool SqT, SqB; public double Sf, Sm; public string Name; public int Qty; }

        // Décrit chaque virole : tube globalement conique (femelle -> mâle), mitres au joint.
        static List<Spec> Specs(Params p, out double Ltot, out double alpha, out double phi)
        {
            int N = Math.Max(2, p.NbSegments);
            phi = (p.Deviation * Math.PI / 180.0) / (N - 1);
            alpha = phi / 2.0;
            double Rm = SolveRm(p);
            double Lend = Rm * Math.Tan(alpha);
            double Lint = 2 * Rm * Math.Tan(alpha);

            var lens = new double[N];
            for (int k = 0; k < N; k++) lens[k] = (k == 0 || k == N - 1) ? Lend : Lint;
            double total = 0; foreach (var l in lens) total += l;
            Ltot = total;

            double Rfem = RFem(p);
            double Rmal = RMal(p);
            double Rad(double s) => Rfem + (Rmal - Rfem) * (s / total);

            var s = new double[N + 1];
            for (int k = 0; k < N; k++) s[k + 1] = s[k] + lens[k];

            string fem = p.AlignFourreau
                ? $"BOUT FEMELLE  Ø{p.Diametre:0} fourreau (Ø réel {2*Rfem:0.#})"
                : $"BOUT FEMELLE  Ø{p.Diametre:0} exact";
            var specs = new List<Spec>();
            for (int k = 0; k < N; k++)
            {
                string name = (k == 0) ? fem
                            : (k == N - 1) ? $"BOUT MALE  -{p.Reduction:0}mm circ (Ø réel {2*Rmal:0.#})"
                            : "INTERMEDIAIRE";
                specs.Add(new Spec {
                    Rf = Rad(s[k]), r = Rad(s[k + 1]), L = lens[k],
                    SqT = (k == 0), SqB = (k == N - 1),
                    Sf = s[k], Sm = s[k + 1], Name = name, Qty = 1
                });
            }
            return specs;
        }

        static V3 SolveZ(double Rf, double r, double L, double alpha, double theta, bool top)
        {
            double m = (Rf - r) / L, b = (Rf + r) / 2.0, c = Math.Cos(theta);
            double sa = Math.Sin(alpha), ca = Math.Cos(alpha), z;
            if (top) z = (ca * L / 2 + sa * b * c) / (ca - sa * m * c);
            else     z = (-ca * L / 2 - sa * b * c) / (ca + sa * m * c);
            double rho = m * z + b;
            return new V3(rho * c, rho * Math.Sin(theta), z);
        }

        static (V3[] F, V3[] M) Gore3D(Spec sp, double alpha, int ndiv)
        {
            var F = new V3[ndiv + 1]; var M = new V3[ndiv + 1];
            for (int i = 0; i <= ndiv; i++)
            {
                double th = 2 * Math.PI * i / ndiv;
                F[i] = SolveZ(sp.Rf, sp.r, sp.L, sp.SqT ? 0 : alpha, th, true);
                M[i] = SolveZ(sp.Rf, sp.r, sp.L, sp.SqB ? 0 : alpha, th, false);
            }
            return (F, M);
        }

        // ------------------------------------------------------------------
        //  Développement EXACT d'une virole (tronc de cône, ou cylindre si
        //  réduction nulle) coupée par ses 2 plans (droit ou onglet).
        //  Point (θ, z) -> plan : génératrice θ = rayon issu du sommet du
        //  cône, à l'angle ψ = k·θ (k = sin du demi-angle au sommet).
        //  Écrit sous forme bien conditionnée (pas de soustraction de grands
        //  nombres quand le cône est presque un cylindre).
        //  Repère : bord femelle à θ=0 à l'origine, θ croissant vers +X,
        //  bord femelle en haut.
        // ------------------------------------------------------------------
        sealed class Dev
        {
            readonly double m, b, L, k, sq, Rf, r, aT, aB, z0, rho0;
            public Dev(Spec sp, double alpha)
            {
                Rf = sp.Rf; r = sp.r; L = sp.L;
                m = (Rf - r) / L; b = (Rf + r) / 2.0;
                sq = Math.Sqrt(1 + m * m); k = m / sq;
                aT = sp.SqT ? 0 : alpha; aB = sp.SqB ? 0 : alpha;
                z0 = Z(0, true, out _); rho0 = m * z0 + b;
            }
            // Cote axiale du bord (plan droit ou d'onglet) et dérivée dz/dθ.
            double Z(double th, bool top, out double dz)
            {
                double a = top ? aT : aB, sa = Math.Sin(a), ca = Math.Cos(a), c = Math.Cos(th), s = Math.Sin(th);
                if (top) { double den = ca - sa * m * c; dz = -s * sa * ca * Rf / (den * den); return (ca * L / 2 + sa * b * c) / den; }
                else     { double den = ca + sa * m * c; dz =  s * sa * ca * r  / (den * den); return -(ca * L / 2 + sa * b * c) / den; }
            }
            public double Rho(double th, bool top) => m * Z(th, top, out _) + b;
            static double Sinc(double x) => Math.Abs(x) < 1e-4 ? 1 - x * x / 6 : Math.Sin(x) / x;
            public V2 Pt(double th, bool top)
            {
                double z = Z(th, top, out _), rho = m * z + b, kt = k * th, s2 = Sinc(kt / 2);
                return new V2(rho * th * Sinc(kt), sq * (z - z0) * Math.Cos(kt) - rho0 * (k * th * th / 2) * s2 * s2);
            }
            public V2 Tan(double th, bool top)   // dérivée exacte d(Pt)/dθ
            {
                double z = Z(th, top, out double dz), rho = m * z + b, ps = k * th, s = Math.Sin(ps), c = Math.Cos(ps);
                return new V2(sq * dz * s + rho * c, sq * dz * c - rho * s);
            }
        }

        /// <summary>Développés à plat de toutes les viroles (P1 = bout femelle ... PN = bout mâle).</summary>
        public static List<Gore> BuildElbow(Params p)
        {
            var specs = Specs(p, out _, out double alpha, out _);
            int nd = Math.Max(24, p.NdivDev); if (nd % 2 == 1) nd++;   // pair => gorge pile à θ = π
            const double TWO_PI = 2 * Math.PI;

            var gores = new List<Gore>();
            int N = specs.Count, mid = 0;
            for (int k = 0; k < N; k++)
            {
                var sp = specs[k];
                var dv = new Dev(sp, alpha);

                // Redressement : corde du bord femelle horizontale (parallèle à l'écran).
                V2 e = dv.Pt(TWO_PI, true);
                double ang = Math.Atan2(e.Y, e.X), ca = Math.Cos(-ang), sa = Math.Sin(-ang);
                V2 Rot(V2 v) => new V2(v.X * ca - v.Y * sa, v.X * sa + v.Y * ca);
                Func<double, V2> fT = th => Rot(dv.Pt(th, true)),  tT = th => Rot(dv.Tan(th, true));
                Func<double, V2> fB = th => Rot(dv.Pt(th, false)), tB = th => Rot(dv.Tan(th, false));

                var F2 = new V2[nd + 1]; var M2 = new V2[nd + 1];
                for (int i = 0; i <= nd; i++) { double th = TWO_PI * i / nd; F2[i] = fT(th); M2[i] = fB(th); }

                string nm = sp.Name, tag;
                if (k == 0) tag = "FEMELLE";
                else if (k == N - 1) tag = "MALE";
                else { mid++; nm = $"INTERMEDIAIRE {mid}"; tag = $"INTER{mid}"; }

                // Recouvrement dégradé : bout femelle 20→joint 25, bout mâle 25→35
                double recF, recM;
                if (k == 0)           { recF = p.RecBoutFemelle; recM = p.RecJoint;     } // bout femelle
                else if (k == N - 1)  { recF = p.RecJoint;       recM = p.RecBoutMale;  } // bout mâle
                else                  { recF = p.RecJoint;       recM = p.RecJoint;     } // intermédiaires

                // La patte de recouvrement PROLONGE les bords sur la vraie courbe (θ > 2π) :
                // une fois roulée, elle tombe pile sur le début de la virole (pas de dépassement
                // à l'onglet) et le bord reste UNE courbe continue jusqu'au bout de patte.
                double thF = TWO_PI + recF / dv.Rho(0, true);
                double thM = TWO_PI + recM / dv.Rho(0, false);

                // Contour fermé : bord femelle (0 -> bout de patte), bout de patte (droite),
                // bord mâle à l'envers (bout de patte -> 0), couture d'origine (droite).
                var cont = new List<BVertex>();
                AppendCurve(cont, fT, tT, 0, thF, p.TolArc);
                cont.Add(new BVertex(fT(thF), 0));
                AppendCurve(cont, fB, tB, thM, 0, p.TolArc);
                cont.Add(new BVertex(fB(0), 0));

                // Zone de recouvrement (aperçu)
                var flap = new List<V2>();
                const int NF = 24;
                for (int i = 0; i <= NF; i++) flap.Add(fT(TWO_PI + (thF - TWO_PI) * i / NF));
                for (int i = NF; i >= 0; i--) flap.Add(fB(TWO_PI + (thM - TWO_PI) * i / NF));

                int arcs = 0, lines = 0;
                foreach (var v in cont) { if (v.Bulge != 0) arcs++; else lines++; }

                gores.Add(new Gore {
                    Female = F2, Male = M2, Name = nm, Tag = tag, PartNo = k + 1, Qty = 1,
                    HeelIndex = 0, ThroatIndex = nd / 2,
                    Width = (F2[nd] - F2[0]).Norm(),
                    DevHaut = ArcLen(tT, 0, thF), DevBas = ArcLen(tB, 0, thM),
                    SeamRecF = recF, SeamRecM = recM,
                    Contour = cont, ContourPts = Tessellate(cont).ToArray(), Flap = flap.ToArray(),
                    SeamF = F2[nd], SeamM = M2[nd], FlapF = fT(thF), FlapM = fB(thM),
                    ArcCount = arcs, LineCount = lines,
                    MaxKinkDeg = MaxKink(cont)
                });
            }
            return gores;
        }

        // Longueur d'arête développée = ∫|dP/dθ| dθ (Simpson sur la dérivée exacte).
        static double ArcLen(Func<double, V2> tf, double t0, double t1)
        {
            const int n = 2000; double h = (t1 - t0) / n, s = tf(t0).Norm() + tf(t1).Norm();
            for (int i = 1; i < n; i++) s += (i % 2 == 1 ? 4 : 2) * tf(t0 + i * h).Norm();
            return Math.Abs(s * h / 3);
        }

        // Échantillonne une arête développée (courbe exacte + tangentes exactes) et la
        // convertit en chaîne d'arcs tangents (biarcs G1). Ajoute les sommets de départ ;
        // le point final est porté par le sommet suivant du contour.
        static void AppendCurve(List<BVertex> outV, Func<double, V2> f, Func<double, V2> tf,
                                double t0, double t1, double tol)
        {
            if (Math.Abs(t1 - t0) < 1e-12) return;
            const int NC = 256; double len = 0; V2 prev = f(t0);
            for (int i = 1; i <= NC; i++) { V2 q = f(t0 + (t1 - t0) * i / NC); len += (q - prev).Norm(); prev = q; }
            int n = (int)Math.Ceiling(len / 0.5);                 // 1 échantillon / 0,5 mm
            n = Math.Max(64, Math.Min(6000, n));
            double sgn = t1 >= t0 ? 1 : -1;
            var P = new V2[n + 1]; var T = new V2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double th = t0 + (t1 - t0) * i / n;
                P[i] = f(th); T[i] = tf(th).Unit() * sgn;
            }
            var loc = new List<BVertex>();
            ArcFit.Fit(P, T, tol, loc);
            ArcFit.Merge(loc, P[n]);
            outV.AddRange(loc);
        }

        /// <summary>Discrétise un contour à bulges (fermé) pour l'aperçu / encombrement.</summary>
        public static List<V2> Tessellate(List<BVertex> v, double maxStepDeg = 2.0)
        {
            var o = new List<V2>();
            int n = v.Count;
            for (int i = 0; i < n; i++)
            {
                V2 a = v[i].P, b = v[(i + 1) % n].P; double bu = v[i].Bulge;
                o.Add(a);
                if (bu == 0) continue;
                ArcFit.Circle(a, b, bu, out V2 C, out double R);
                double sw = 4 * Math.Atan(bu), a0 = Math.Atan2(a.Y - C.Y, a.X - C.X);
                int st = Math.Max(1, (int)Math.Ceiling(Math.Abs(sw) * 180 / Math.PI / maxStepDeg));
                st = Math.Max(st, Math.Min(400, (int)Math.Ceiling(Math.Abs(sw) * R / 2.0)));   // + pas ≤ 2 mm
                for (int j = 1; j < st; j++)
                {
                    double an = a0 + sw * j / st;
                    o.Add(new V2(C.X + R * Math.Cos(an), C.Y + R * Math.Sin(an)));
                }
            }
            return o;
        }

        // Plus grande cassure de tangente entre tronçons, en ignorant les 4 vrais coins
        // de la pièce (cassures > 20°). Doit rester ~0 : sinon l'outillage marquerait un coin.
        static double MaxKink(List<BVertex> v)
        {
            int n = v.Count; double mx = 0;
            for (int i = 0; i < n; i++)
            {
                var pr = v[(i - 1 + n) % n]; var cu = v[i]; var nx = v[(i + 1) % n];
                V2 tin  = ArcFit.EndTangent(pr.P, cu.P, pr.Bulge);
                V2 tout = ArcFit.StartTangent(cu.P, nx.P, cu.Bulge);
                double d = Math.Abs(Math.Atan2(V2.Cross(tin, tout), V2.Dot(tin, tout))) * 180 / Math.PI;
                if (d < 20 && d > mx) mx = d;
            }
            return mx;
        }

        /// <summary>Modèle 3D assemblé du coude (tube conique mitré).</summary>
        public static List<Band3D> BuildElbow3D(Params p)
        {
            var specs = Specs(p, out _, out double alpha, out double phi);
            int N = specs.Count;
            var bands = new List<Band3D>();

            var u = new V3[N]; var h = new V3[N]; var P = new V3[N + 1];
            for (int k = 0; k < N; k++)
            {
                u[k] = new V3(Math.Cos(k * phi), 0, -Math.Sin(k * phi));
                h[k] = new V3(Math.Sin(k * phi), 0,  Math.Cos(k * phi));
            }
            P[0] = new V3(0, 0, 0);
            for (int k = 0; k < N; k++) P[k + 1] = P[k] + u[k] * specs[k].L;

            for (int k = 0; k < N; k++)
            {
                var (F, M) = Gore3D(specs[k], alpha, p.NdivDev);
                V3 C = (P[k] + P[k + 1]) * 0.5;
                V3 Apply(V3 v) => h[k] * v.X + new V3(0, v.Y, 0) + (u[k] * (-v.Z)) + C; // x->h, y->y, z->-u
                var Fg = new V3[F.Length]; var Mg = new V3[M.Length];
                for (int i = 0; i < F.Length; i++) { Fg[i] = Apply(F[i]); Mg[i] = Apply(M[i]); }
                bands.Add(new Band3D { Fem = Fg, Mal = Mg });
            }
            return bands;
        }
    }

    // ---------------------------------------------------------------------
    //  ArcFit : courbe -> chaîne d'arcs TANGENTS (biarcs G1).
    //  Chaque biarc part d'un point exact de la courbe avec sa tangente
    //  exacte et arrive sur le suivant avec la sienne : aucune cassure entre
    //  arcs, donc l'AutoPunch grignote d'un trait au lieu de faire du
    //  pas-à-pas sur des centaines de petits segments.
    //  Subdivision récursive tant que l'écart à la courbe > tol.
    // ---------------------------------------------------------------------
    public static class ArcFit
    {
        const double MaxBulge = 0.41421356237;   // tan(22,5°) => arcs ≤ 90°

        public static void Fit(V2[] P, V2[] T, double tol, List<BVertex> outV)
        {
            if (P.Length < 2) return;
            Rec(P, T, 0, P.Length - 1, Math.Max(1e-4, tol), outV);
        }

        static void Rec(V2[] P, V2[] T, int i0, int i1, double tol, List<BVertex> outV)
        {
            if (Biarc(P[i0], T[i0], P[i1], T[i1], out V2 J, out double b1, out double b2)
                && Math.Abs(b1) <= MaxBulge && Math.Abs(b2) <= MaxBulge
                && (i1 - i0 <= 1 || MaxErr(P, i0, i1, J, b1, b2) <= tol))
            {
                outV.Add(new BVertex(P[i0], b1));
                outV.Add(new BVertex(J, b2));
                return;
            }
            if (i1 - i0 <= 1) { outV.Add(new BVertex(P[i0], 0)); return; }
            int im = (i0 + i1) / 2;
            Rec(P, T, i0, im, tol, outV);
            Rec(P, T, im, i1, tol, outV);
        }

        // Biarc "d égaux" : P0 (tangente T0) -> J -> P1 (tangente T1), tangent en J.
        static bool Biarc(V2 p0, V2 t0, V2 p1, V2 t1, out V2 J, out double b1, out double b2)
        {
            J = p0; b1 = b2 = 0;
            V2 v = p1 - p0, t = t0 + t1;
            double vv = V2.Dot(v, v), vt = V2.Dot(v, t), a = V2.Dot(t, t) - 4;   // a ≤ 0
            if (vv < 1e-18) return false;
            double disc = vt * vt - a * vv; if (disc < 0) return false;
            double den = vt + Math.Sqrt(disc);
            if (den < 1e-12) return false;
            double d = vv / den;                       // racine stable de a·d² - 2·vt·d + vv = 0
            if (!(d > 0) || double.IsInfinity(d)) return false;
            V2 q0 = p0 + t0 * d, q1 = p1 - t1 * d;
            J = (q0 + q1) * 0.5;
            V2 tJ = (q1 - q0).Unit();
            b1 = BulgeFromTangent(p0, t0, J);
            b2 = BulgeFromTangent(J, tJ, p1);
            return !double.IsNaN(b1) && !double.IsNaN(b2);
        }

        static double BulgeFromTangent(V2 a, V2 t, V2 b)
        {
            V2 c = b - a;
            double g = Math.Atan2(V2.Cross(t, c), V2.Dot(t, c));   // demi-balayage
            double bu = Math.Tan(g / 2);
            return Math.Abs(bu) < 1e-12 ? 0 : bu;
        }

        static double MaxErr(V2[] P, int i0, int i1, V2 J, double b1, double b2)
        {
            double mx = 0;
            for (int i = i0 + 1; i < i1; i++)
            {
                double d = Math.Min(DistArc(P[i], P[i0], J, b1), DistArc(P[i], J, P[i1], b2));
                if (d > mx) mx = d;
            }
            return mx;
        }

        public static void Circle(V2 a, V2 b, double bulge, out V2 C, out double R)
        {
            V2 c = b - a, mid = (a + b) * 0.5, perp = new V2(-c.Y, c.X);
            C = mid + perp * ((1 - bulge * bulge) / (4 * bulge));
            R = (a - C).Norm();
        }

        public static V2 StartTangent(V2 a, V2 b, double bulge)
        {
            V2 c = (b - a).Unit(); double g = 2 * Math.Atan(bulge);       // demi-balayage
            double cg = Math.Cos(-g), sg = Math.Sin(-g);
            return new V2(c.X * cg - c.Y * sg, c.X * sg + c.Y * cg);
        }
        public static V2 EndTangent(V2 a, V2 b, double bulge)
        {
            V2 c = (b - a).Unit(); double g = 2 * Math.Atan(bulge);
            double cg = Math.Cos(g), sg = Math.Sin(g);
            return new V2(c.X * cg - c.Y * sg, c.X * sg + c.Y * cg);
        }

        static double DistSeg(V2 q, V2 a, V2 b)
        {
            V2 ab = b - a; double l2 = V2.Dot(ab, ab);
            double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, V2.Dot(q - a, ab) / l2));
            return (q - (a + ab * t)).Norm();
        }

        static double DistArc(V2 q, V2 a, V2 b, double bulge)
        {
            if (Math.Abs(bulge) < 1e-9) return DistSeg(q, a, b);
            Circle(a, b, bulge, out V2 C, out double R);
            double sw = 4 * Math.Atan(bulge);
            double rel = Math.Atan2(q.Y - C.Y, q.X - C.X) - Math.Atan2(a.Y - C.Y, a.X - C.X);
            const double TP = 2 * Math.PI;
            bool inside;
            if (sw > 0) { rel = ((rel % TP) + TP) % TP; inside = rel <= sw; }
            else        { rel = -((((-rel) % TP) + TP) % TP); inside = rel >= sw; }
            if (inside) return Math.Abs((q - C).Norm() - R);
            return Math.Min((q - a).Norm(), (q - b).Norm());
        }

        /// <summary>Fusionne les tronçons consécutifs sur le même cercle (ou la même droite).</summary>
        public static void Merge(List<BVertex> v, V2 end)
        {
            int i = 0;
            while (i + 1 < v.Count)
            {
                V2 a = v[i].P, m = v[i + 1].P, c = (i + 2 < v.Count) ? v[i + 2].P : end;
                if (CanMerge(a, v[i].Bulge, m, v[i + 1].Bulge, c, out double nb))
                { v[i] = new BVertex(a, nb); v.RemoveAt(i + 1); }
                else i++;
            }
        }

        static bool CanMerge(V2 a, double b1, V2 m, double b2, V2 c, out double nb)
        {
            nb = 0;
            bool s1 = b1 == 0, s2 = b2 == 0;
            if (s1 && s2)
            {
                V2 d1 = m - a, d2 = c - m; double l = d1.Norm() * d2.Norm();
                return l > 0 && Math.Abs(V2.Cross(d1, d2)) <= 1e-9 * l && V2.Dot(d1, d2) > 0;
            }
            if (s1 || s2 || Math.Sign(b1) != Math.Sign(b2)) return false;
            Circle(a, m, b1, out V2 C1, out double R1);
            Circle(m, c, b2, out V2 C2, out double R2);
            double tc = 1e-7 * Math.Max(1, R1);
            if ((C1 - C2).Norm() > tc || Math.Abs(R1 - R2) > tc) return false;
            double sw = 4 * Math.Atan(b1) + 4 * Math.Atan(b2);
            if (Math.Abs(sw) > Math.PI * 0.999) return false;   // on garde des arcs < 180°
            nb = Math.Tan(sw / 4);
            return true;
        }
    }
}
