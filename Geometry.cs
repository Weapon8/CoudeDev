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
        public double MinArcLen;                     // plus court tronçon des 2 bords courbes (mm)
        public double MaxDev;                        // écart maxi mesuré contour / courbe exacte (mm)
        public double ArcMiniApplique;               // arc mini réellement tenu (= ArcMini sauf si impossible)
        public double TolAppliquee;                  // tolérance réellement utilisée (> TolArc si montée auto)

        // --- Trous repère (calque DECOUPE, CIRCLE) : nb = n° de pièce, dans la patte, côté MÂLE ---
        public List<V2> Trous = new();               // centres des trous (vide si option décochée)
        public double TrouDiam;                      // Ø des trous (mm)
        public string? TrouAlerte;                   // non null si le code n'a pas pu être posé tel quel
        public List<V2> TrousTS = new();             // les mêmes sur le cône : X = θ de la génératrice, Y = distance au bord bas (3D)
    }

    /// <summary>Bande 3D assemblée (bague femelle + bague mâle dans le repère du coude).</summary>
    public class Band3D
    {
        public V3[] Fem = Array.Empty<V3>(); public V3[] Mal = Array.Empty<V3>();
        public V3 SeamN;                                // normale extérieure à la couture (θ = 0, génératrice Fem[0]-Mal[0])
        public V3[] Trous = Array.Empty<V3>();          // trous repère (posés sur la patte, 1 mm au-dessus)
        public V3[] TrousN = Array.Empty<V3>();         // normales extérieures des trous
    }

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
            /// <summary>true (défaut) : le mâle suit la femelle (même Ø, corps femelle − Reduction).
            /// false : Ø mâle SAISI (coude réducteur, ex. femelle Ø200 / mâle Ø160, reprise d'une ancienne
            /// descente Ø140 -> Ø120) : le bout mâle est celui d'un coude de ce Ø avec les mêmes réglages
            /// (barème ou π·Ø, − recouv. femelle, − Reduction).</summary>
            public bool MaleSuitFemelle = true;
            /// <summary>Ø nominal côté mâle (mm), utilisé si MaleSuitFemelle = false.</summary>
            public double DiamMale = 160;
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
            /// <summary>Écart visé contour d'arcs / courbe exacte (mm). Calé sur la dentelure Metalix
            /// (0,2 mm à l'atelier) : le contour ne s'écarte pas plus que les festons du grignotage.
            /// L'ancien réglage 0,02 sortait ~60 arcs de 10 mm par pièce, que Metalix ne grignotait pas.</summary>
            public double TolArc         = 0.2;
            /// <summary>Écart MAXI accepté (mm) : si l'arc mini ne tient pas à TolArc (petit Ø, coude
            /// serré), la tolérance monte toute seule par paliers jusqu'ici avant de raccourcir un arc.</summary>
            public double TolArcMax      = 0.5;
            /// <summary>Longueur MINI d'un arc du contour DECOUPE (mm) = Ø du rond de grignotage
            /// (rond de 20 à l'atelier). Aucun arc plus court au DXF, pour que Metalix les grignote tous.</summary>
            public double ArcMini        = 20;

            /// <summary>Trous repère poinçonnés au rond standard (pas besoin d'outil de marquage) :
            /// nombre de trous = n° de pièce (P1 femelle = 1 ... PN mâle = N), toujours dans la patte
            /// de recouvrement, côté MÂLE de la virole. Une fois roulé, l'autre bord les recouvre.</summary>
            public bool TrousRepere      = true;
            /// <summary>Ø des trous repère (mm) = rond de la tourelle (rond de 6 à l'atelier).</summary>
            public double TrouDiam       = 6;
            /// <summary>Centre du 1er trou à X mm du bout de patte (le long du bord) et du bord côté mâle.</summary>
            public double TrouBord       = 10;
            /// <summary>Entraxe des trous suivants, empilés le long de la génératrice (mm).</summary>
            public double TrouPas        = 15;

            /// <summary>Coude contre coude (dévoiement en Z / S, ex. descente EP) : 2 coudes IDENTIQUES,
            /// le 2e tourné de 180° autour de son axe et emboîté sur le mâle du 1er. Le rayon de cintrage
            /// (donc le bras ext.) est CALCULÉ pour tenir le déport voulu ; LongueurBrasExt est alors ignoré.</summary>
            public bool CoudeContreCoude = false;
            /// <summary>Déport = distance entre l'axe d'entrée et l'axe de sortie (parallèles), mm.</summary>
            public double Deport         = 300;
            /// <summary>Longueur d'emboîtement du mâle du coude 1 dans la femelle du coude 2 (mm).</summary>
            public double EmboitementZ   = 30;
            /// <summary>Coude contre coude en UNE pièce (S, élément central) : le mâle du coude 1 et la
            /// femelle du coude 2 fusionnent en une virole centrale à onglets OPPOSÉS -> 2N−1 viroles
            /// (3 en 2 éléments), cône continu de bout en bout, couture sur le côté (alignée sur tout le S).
            /// false = 2 coudes séparés emboîtés (chaque pièce ×2).</summary>
            public bool ZUnePiece        = false;

            /// <summary>Déviation de l'axe = angle dont tourne le tube = 180 - angle réel.</summary>
            public double Deviation => 180.0 - AngleReel;

            /// <summary>Copie (pour tester une variante, ex. un élément de plus).</summary>
            public Params Clone() => (Params)MemberwiseClone();
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
        /// <summary>Corps (circonférence roulée) côté femelle d'un coude de Ø d avec ces réglages :
        /// barème fourreaux − recouv. femelle, ou π·d. CorpsPourDiam(p, Diametre) = CircFemelle(p).</summary>
        public static double CorpsPourDiam(Params p, double d) =>
            p.AlignFourreau ? BaremeFourreaux.Dev(d) - p.RecBoutFemelle : Math.PI * d;
        /// <summary>Circonférence côté mâle = corps (Ø femelle, ou Ø mâle saisi) − réduction X.</summary>
        public static double CircMale(Params p) =>
            (p.MaleSuitFemelle ? CircFemelle(p) : CorpsPourDiam(p, p.DiamMale)) - p.Reduction;
        /// <summary>Réduction réellement appliquée (mm de circonférence) : saisie, ou déduite du dév. mâle saisi.</summary>
        public static double ReductionEff(Params p) => CircFemelle(p) - CircMale(p);
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
            if (!p.MaleSuitFemelle)
            {
                if (!(p.DiamMale > 0)) return "Ø mâle > 0.";
                if (p.DiamMale > p.Diametre + 1e-9) return "Ø mâle plus grand que le Ø femelle :\nle mâle est le petit côté (femelle = grand côté).";
            }
            if (p.Reduction < 0) return "Réduction mâle ≥ 0.";
            if (ReductionEff(p) >= CircFemelle(p) * 0.5)
                return $"Réduction mâle hors limites : {ReductionEff(p):0} mm circ au total\n(maxi {CircFemelle(p) * 0.5:0} mm = 50 % de la circonférence).\nRéduction habituelle : 25 mm.";
            if (p.CoudeContreCoude)
            {
                if (!(p.Deport > 0)) return "Coude contre coude : déport > 0.";
                if (!(p.EmboitementZ >= 0)) return "Coude contre coude : emboîtement ≥ 0.";
                if (!(RmPourDeport(p) > RFem(p) + 0.5))
                    return $"Déport trop petit pour 2 coudes l'un contre l'autre.\nMini ≈ {DeportMini(p) + 1:0} mm à cet angle\n(angle réel plus ouvert = déport mini plus petit).";
            }
            else if (!(p.LongueurBrasExt > 0)) return "Longueur bras ext. > 0.";
            if (!(p.TolArc >= 0.01 && p.TolArc <= 2)) return "Tolérance contour : entre 0,01 et 2 mm.";
            if (!(p.TolArcMax >= p.TolArc && p.TolArcMax <= 2)) return "Tolérance maxi : entre la tolérance contour et 2 mm.";
            if (!(p.ArcMini >= 0 && p.ArcMini <= 500)) return "Arc mini : entre 0 et 500 mm.";
            if (p.TrousRepere)
            {
                if (!(p.TrouDiam >= 1 && p.TrouDiam <= 30)) return "Ø trou repère : entre 1 et 30 mm.";
                if (!(p.TrouBord >= p.TrouDiam / 2 + 1)) return $"Trou repère : distance au bord ≥ {p.TrouDiam / 2 + 1:0.#} mm (Ø/2 + 1).";
                if (!(p.TrouPas >= p.TrouDiam + 1)) return $"Entraxe trous repère ≥ {p.TrouDiam + 1:0.#} mm (Ø + 1).";
            }
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
            if (UnePiece(p)) Ltot = 2 * Lend + (2 * N - 3) * Lint;    // S d'une pièce : le cône court sur tout le S
            if (Ltot < 1e-6) return 0;
            double Rfem = RFem(p);
            double Rmal = RMal(p);
            double Rf = Rfem, r = Rfem + (Rmal - Rfem) * (Lend / Ltot);
            V3 F0 = SolveZ(Rf, r, Lend, 0,  0, true);    // haut droit (femelle)
            V3 M0 = SolveZ(Rf, r, Lend, al, 0, false);   // bas onglet, extrados
            return (F0 - M0).Norm();
        }

        // ---- Coude contre coude (Z) ----
        // Axe d'un coude = polyligne de N tronçons (Lend, Lint..., Lend) proportionnels à Rm.
        // Renvoie Σ (L_k/Rm)·cos(kφ) et Σ (L_k/Rm)·sin(kφ) : avancée axiale / latérale d'UN coude pour Rm = 1.
        static (double C1, double S1) AxeUnitaire(Params p)
        {
            int N = Math.Max(2, p.NbSegments);
            double al = AlphaRad(p), phi = 2 * al, t = Math.Tan(al), C = 0, S = 0;
            for (int k = 0; k < N; k++)
            {
                double l = (k == 0 || k == N - 1) ? t : 2 * t;
                C += l * Math.Cos(k * phi); S += l * Math.Sin(k * phi);
            }
            return (C, S);
        }
        static double DevRad(Params p) => p.Deviation * Math.PI / 180.0;

        /// <summary>S d'une pièce (élément central) : true. 2 coudes emboîtés ou coude normal : false.</summary>
        public static bool UnePiece(Params p) => p.CoudeContreCoude && p.ZUnePiece;
        // Emboîtement réellement appliqué : seulement entre 2 coudes séparés (pas dans un S d'une pièce).
        static double EmbEff(Params p) => p.CoudeContreCoude && !p.ZUnePiece ? p.EmboitementZ : 0;
        /// <summary>Position de la couture (angle depuis l'extrados du 1er coude) : 0 = extrados (coude
        /// normal, 2 coudes) ; π/2 = sur le côté (S d'une pièce) -> couture alignée d'un bout à l'autre.</summary>
        public static double SeamAng(Params p) => UnePiece(p) ? Math.PI / 2 : 0;

        /// <summary>Déport obtenu avec 2 coudes de rayon d'axe Rm, l'un contre l'autre :
        /// 2·Rm·S1 (les 2 coudes) − emboîtement·sin(déviation) (le coude 2 recule le long du joint).
        /// S d'une pièce : même axe, sans emboîtement.</summary>
        public static double DeportZ(Params p, double Rm) =>
            2 * Rm * AxeUnitaire(p).S1 - EmbEff(p) * Math.Sin(DevRad(p));

        /// <summary>Rayon d'axe qui donne exactement le déport voulu (formule fermée : le déport est linéaire en Rm).</summary>
        public static double RmPourDeport(Params p)
        {
            double S1 = AxeUnitaire(p).S1;
            return S1 < 1e-9 ? 0 : (p.Deport + EmbEff(p) * Math.Sin(DevRad(p))) / (2 * S1);
        }

        /// <summary>Déport mini en coude contre coude (gorge nulle au bout femelle).</summary>
        public static double DeportMini(Params p) => DeportZ(p, RFem(p) + 0.5);

        /// <summary>Hauteur du dévoiement = distance axiale entre le centre de l'entrée (femelle coude 1)
        /// et le centre de la sortie (mâle coude 2).</summary>
        public static double HauteurZ(Params p) =>
            2 * SolveRm(p) * AxeUnitaire(p).C1 - EmbEff(p) * Math.Cos(DevRad(p));

        /// <summary>Longueur extrados du bras correspondant au rayon utilisé (calculée en mode Z).</summary>
        public static double BrasExtCalcule(Params p) => HeelFemelle(p, SolveRm(p));

        // Trouve Rm pour que l'extrados du bras = LongueurBrasExt (bissection).
        // En coude contre coude : Rm vient directement du déport.
        static double SolveRm(Params p)
        {
            if (p.CoudeContreCoude) return Math.Max(0.5, RmPourDeport(p));
            double al = AlphaRad(p);
            double lo = 0.5, hi = p.LongueurBrasExt / Math.Max(1e-3, Math.Tan(al)) + p.Diametre + 10;
            for (int it = 0; it < 60; it++)
            {
                double mid = (lo + hi) / 2;
                if (HeelFemelle(p, mid) < p.LongueurBrasExt) lo = mid; else hi = mid;
            }
            return Math.Max(0.5, (lo + hi) / 2);
        }

        // PhT / PhB : phase de l'onglet haut / bas. 0 = talon (côté long) à θ = 0 (sens du 1er coude) ;
        // π = talon à θ = π (2e coude d'un S d'une pièce, qui tourne dans l'autre sens).
        // Ang : orientation de l'axe de la virole (angle cumulé dans le plan du coude).
        struct Spec { public double Rf, r, L; public bool SqT, SqB; public double PhT, PhB, Ang; public bool Centre;
                      public double Sf, Sm; public string Name; public int Qty; }

        // Décrit chaque virole : tube globalement conique (femelle -> mâle), mitres au joint.
        // Coude normal / 2 coudes : N viroles. S d'une pièce : 2N−1 viroles, la virole centrale
        // (= mâle du coude 1 + femelle du coude 2) fait 2·Lend = Lint, onglet haut phase 0, bas phase π.
        static List<Spec> Specs(Params p, out double Ltot, out double alpha, out double phi)
        {
            int N = Math.Max(2, p.NbSegments);
            phi = (p.Deviation * Math.PI / 180.0) / (N - 1);
            alpha = phi / 2.0;
            double Rm = SolveRm(p);
            double Lend = Rm * Math.Tan(alpha);
            double Lint = 2 * Rm * Math.Tan(alpha);
            bool s1 = UnePiece(p);
            int M = s1 ? 2 * N - 1 : N;

            var lens = new double[M];
            for (int k = 0; k < M; k++) lens[k] = (k == 0 || k == M - 1) ? Lend : Lint;
            double total = 0; foreach (var l in lens) total += l;
            Ltot = total;

            double Rfem = RFem(p);
            double Rmal = RMal(p);
            double Rad(double s) => Rfem + (Rmal - Rfem) * (s / total);

            var s = new double[M + 1];
            for (int k = 0; k < M; k++) s[k + 1] = s[k] + lens[k];

            string fem = p.AlignFourreau
                ? $"BOUT FEMELLE  Ø{p.Diametre:0} fourreau (Ø réel {2*Rfem:0.#})"
                : $"BOUT FEMELLE  Ø{p.Diametre:0} exact";
            var specs = new List<Spec>();
            for (int k = 0; k < M; k++)
            {
                bool centre = s1 && k == N - 1;
                string name = (k == 0) ? fem
                            : (k == M - 1) ? (p.MaleSuitFemelle
                                ? $"BOUT MALE  -{p.Reduction:0}mm circ (Ø réel {2*Rmal:0.#})"
                                : $"BOUT MALE  Ø{p.DiamMale:0} -{p.Reduction:0}mm circ (Ø réel {2*Rmal:0.#})")
                            : centre ? "ELEMENT CENTRAL (onglets opposés)"
                            : "INTERMEDIAIRE";
                specs.Add(new Spec {
                    Rf = Rad(s[k]), r = Rad(s[k + 1]), L = lens[k],
                    SqT = (k == 0), SqB = (k == M - 1),
                    // joint (k-1,k) au 2e coude si k ≥ N ; joint (k,k+1) au 2e coude si k ≥ N-1
                    PhT = (s1 && k >= N) ? Math.PI : 0,
                    PhB = (s1 && k >= N - 1) ? Math.PI : 0,
                    // l'axe tourne de φ à chaque joint, puis revient dans le 2e coude du S
                    Ang = (k <= N - 1 ? k : 2 * (N - 1) - k) * phi,
                    Centre = centre,
                    Sf = s[k], Sm = s[k + 1], Name = name, Qty = 1
                });
            }
            return specs;
        }

        // Point 3D local d'un bord de virole à l'angle GLOBAL thG (0 = extrados du 1er coude),
        // avec la phase de l'onglet (talon à 0 ou à π).
        static V3 Pt3(Spec sp, double alpha, double thG, bool top)
        {
            double a = top ? (sp.SqT ? 0 : alpha) : (sp.SqB ? 0 : alpha);
            double c = Math.Cos(thG + (top ? sp.PhT : sp.PhB));
            double m = (sp.Rf - sp.r) / sp.L, b = (sp.Rf + sp.r) / 2.0, sa = Math.Sin(a), ca = Math.Cos(a), z;
            if (top) z = (ca * sp.L / 2 + sa * b * c) / (ca - sa * m * c);
            else     z = (-ca * sp.L / 2 - sa * b * c) / (ca + sa * m * c);
            double rho = m * z + b;
            return new V3(rho * Math.Cos(thG), rho * Math.Sin(thG), z);
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

        // Bagues 3D d'une virole, θ local depuis la couture (index 0 = couture).
        static (V3[] F, V3[] M) Gore3D(Spec sp, double alpha, int ndiv, double seam)
        {
            var F = new V3[ndiv + 1]; var M = new V3[ndiv + 1];
            for (int i = 0; i <= ndiv; i++)
            {
                double th = seam + 2 * Math.PI * i / ndiv;
                F[i] = Pt3(sp, alpha, th, true);
                M[i] = Pt3(sp, alpha, th, false);
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
        //  Repère : bord femelle à θ=0 (= la couture) à l'origine, θ croissant
        //  vers +X, bord femelle en haut. θ est compté DEPUIS LA COUTURE :
        //  l'onglet voit l'angle θ + couture + phase (talon à 0 ou à π).
        // ------------------------------------------------------------------
        sealed class Dev
        {
            readonly double m, b, L, k, sq, Rf, r, aT, aB, phT, phB, z0, rho0;
            public Dev(Spec sp, double alpha, double seam)
            {
                Rf = sp.Rf; r = sp.r; L = sp.L;
                m = (Rf - r) / L; b = (Rf + r) / 2.0;
                sq = Math.Sqrt(1 + m * m); k = m / sq;
                aT = sp.SqT ? 0 : alpha; aB = sp.SqB ? 0 : alpha;
                phT = seam + sp.PhT; phB = seam + sp.PhB;
                z0 = Z(0, true, out _); rho0 = m * z0 + b;
            }
            // Cote axiale du bord (plan droit ou d'onglet) et dérivée dz/dθ.
            double Z(double th, bool top, out double dz)
            {
                double a = top ? aT : aB, sa = Math.Sin(a), ca = Math.Cos(a), ph = top ? phT : phB,
                       c = Math.Cos(th + ph), s = Math.Sin(th + ph);
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
            double seam = SeamAng(p);
            for (int k = 0; k < N; k++)
            {
                var sp = specs[k];
                var dv = new Dev(sp, alpha, seam);

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
                else if (sp.Centre) tag = "CENTRE";
                else { mid++; nm = $"INTERMEDIAIRE {mid}"; tag = $"INTER{mid}"; }

                // Talon (extrados) / gorge (intrados) côté MÂLE (bord bas) : là où le bord bas est le plus long.
                // Coude normal : talon = couture (θ 0). S d'une pièce : couture sur le côté -> talon à ±90°.
                double thHeel = ((-(seam + sp.PhB)) % TWO_PI + TWO_PI) % TWO_PI;
                int heelIdx = (int)Math.Round(thHeel / TWO_PI * nd) % nd;
                int throatIdx = (heelIdx + nd / 2) % nd;

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
                var stH = AppendCurve(cont, fT, tT, 0, thF, p.TolArc, p.TolArcMax, p.ArcMini);
                cont.Add(new BVertex(fT(thF), 0));
                var stB = AppendCurve(cont, fB, tB, thM, 0, p.TolArc, p.TolArcMax, p.ArcMini);
                cont.Add(new BVertex(fB(0), 0));

                // Zone de recouvrement (aperçu)
                var flap = new List<V2>();
                const int NF = 24;
                for (int i = 0; i <= NF; i++) flap.Add(fT(TWO_PI + (thF - TWO_PI) * i / NF));
                for (int i = NF; i >= 0; i--) flap.Add(fB(TWO_PI + (thM - TWO_PI) * i / NF));

                int arcs = 0, lines = 0;
                foreach (var v in cont) { if (v.Bulge != 0) arcs++; else lines++; }

                // Trous repère : P(k+1) = k+1 trous, dans la patte côté mâle (bord bas)
                var trous = new List<V2>(); string? trouMsg = null;
                if (p.TrousRepere && p.TrouDiam > 0)
                    trouMsg = PlaceTrous(fT, fB, tB, thF, thM, recM, F2[nd], M2[nd], fT(thF), fB(thM), k + 1, p, trous);
                // mêmes trous en coordonnées cône (θ de la génératrice, distance au bord bas) pour la vue 3D
                var trousTS = new List<V2>();
                foreach (var c in trous) trousTS.Add(ToCone(fT, fB, TWO_PI, Math.Max(thF, thM) + 0.05, c));

                gores.Add(new Gore {
                    Female = F2, Male = M2, Name = nm, Tag = tag, PartNo = k + 1,
                    Qty = p.CoudeContreCoude && !p.ZUnePiece ? 2 : 1,           // 2 coudes séparés : chaque pièce ×2
                    HeelIndex = heelIdx, ThroatIndex = throatIdx,
                    Width = (F2[nd] - F2[0]).Norm(),
                    DevHaut = ArcLen(tT, 0, thF), DevBas = ArcLen(tB, 0, thM),
                    SeamRecF = recF, SeamRecM = recM,
                    Contour = cont, ContourPts = Tessellate(cont).ToArray(), Flap = flap.ToArray(),
                    SeamF = F2[nd], SeamM = M2[nd], FlapF = fT(thF), FlapM = fB(thM),
                    ArcCount = arcs, LineCount = lines,
                    MaxKinkDeg = MaxKink(cont),
                    MinArcLen = Math.Min(stH.MinLen, stB.MinLen),
                    MaxDev = Math.Max(stH.MaxDev, stB.MaxDev),
                    ArcMiniApplique = Math.Min(stH.UsedMin, stB.UsedMin),
                    TolAppliquee = Math.Max(stH.UsedTol, stB.UsedTol),
                    Trous = trous, TrouDiam = p.TrouDiam, TrouAlerte = trouMsg, TrousTS = trousTS
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

        // ------------------------------------------------------------------
        //  Trous repère (pas de poinçon de marquage à l'atelier) : on code la pièce
        //  avec des ronds standard, dans la PATTE de recouvrement, côté MÂLE (bord bas).
        //   - colonne de trous PARALLÈLE au bout de patte, à "bord" mm de lui : tous les
        //     trous restent à 10 mm du bord même si la patte se rétrécit en montant
        //     (mâle : 35 en bas -> 25 en haut) ;
        //   - 1er trou à "bord" mm du bord bas, suivants à "pas" d'entraxe vers le haut ;
        //   - nombre de trous = n° de pièce (P1 femelle = 1 ... PN mâle = N).
        //  Contrôles : chaque trou doit être ENTIÈREMENT au-delà de la couture d'origine
        //  (θ = 2π) => recouvert par l'autre bord une fois roulé (pas de trou traversant),
        //  et garder ≥ 1 mm de matière jusqu'au bout de patte.
        //  Renvoie null si OK, sinon un message (trou recentré / code incomplet).
        // ------------------------------------------------------------------
        static string? PlaceTrous(Func<double, V2> fT, Func<double, V2> fB, Func<double, V2> tB,
                                  double thF, double thM, double rec, V2 seamF, V2 seamM, V2 flapF, V2 flapM,
                                  int count, Params p, List<V2> outC)
        {
            double r = p.TrouDiam / 2, along = p.TrouBord;
            string? msg = null;
            // patte trop étroite pour 10 mm : on recentre le trou dans la patte
            if (rec - along < r + 1) { along = rec / 2; msg = $"trou recentré dans la patte de {rec:0} mm"; }
            if (along < r + 1) return $"patte de {rec:0} mm trop étroite pour un trou Ø{p.TrouDiam:0.#} : pas de repère";

            // distances signées : >0 côté patte pour la couture, >0 côté pièce pour le bout de patte
            static double Side(V2 q, V2 a0, V2 a1, V2 inside)
            {
                V2 t = (a1 - a0).Unit();
                double s = V2.Cross(t, q - a0);
                return V2.Cross(t, inside - a0) >= 0 ? s : -s;
            }
            V2 flapMid = (flapF + flapM) * 0.5, seamMid = (seamF + seamM) * 0.5;

            // pied de colonne sur le bord bas : à "along" mm du coin de patte (Newton sur la longueur d'arc)
            double th = thM - along / Math.Max(1e-9, tB(thM).Norm());
            for (int it = 0; it < 5; it++)
                th += (ArcLen(tB, th, thM) - along) / Math.Max(1e-9, tB(th).Norm());
            V2 foot = fB(th);

            // colonne parallèle au bout de patte (bas -> haut), recalée à "along" pile du bout de patte
            V2 u = (flapF - flapM).Unit();
            V2 nIn = new V2(-u.Y, u.X);
            if (V2.Dot(nIn, seamMid - flapM) < 0) nIn = nIn * -1;     // normale vers l'intérieur de la pièce
            foot = foot + nIn * (along - Side(foot, flapM, flapF, seamMid));
            double H = (flapF - flapM).Norm();                         // hauteur dispo le long du bout de patte

            // distance VRAIE (perpendiculaire) d'un point aux bords courbes, sur la zone de la patte
            static double DSeg(V2 q, V2 a, V2 b)
            {
                V2 ab = b - a; double l2 = V2.Dot(ab, ab);
                double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, V2.Dot(q - a, ab) / l2));
                return (q - (a + ab * t)).Norm();
            }
            static double DEdge(Func<double, V2> f, double t0, double t1, V2 q)
            {
                const int NS = 240; double best = double.PositiveInfinity; V2 prev = f(t0);
                for (int i = 1; i <= NS; i++) { V2 cur = f(t0 + (t1 - t0) * i / NS); best = Math.Min(best, DSeg(q, prev, cur)); prev = cur; }
                return best;
            }
            const double TP = 2 * Math.PI;
            double DBas(V2 q) => DEdge(fB, TP - 0.8, thM, q);
            double DHaut(V2 q) => DEdge(fT, TP - 0.8, thF, q);

            // 1er trou à "bord" mm PERPENDICULAIREMENT au bord bas (compte si le bord est incliné,
            // ex. S d'une pièce dont la couture est sur le côté) : bissection sur la hauteur dans la colonne
            double h0 = p.TrouBord;
            if (DBas(foot + u * (3 * p.TrouBord)) > p.TrouBord)
            {
                double lo = 0, hi = 3 * p.TrouBord;
                for (int it = 0; it < 40; it++) { double mid = 0.5 * (lo + hi); if (DBas(foot + u * mid) < p.TrouBord) lo = mid; else hi = mid; }
                h0 = 0.5 * (lo + hi);
            }

            // pose la colonne avec un entraxe donné ; renvoie le nombre de trous qui tiennent
            List<V2> Pose(double pas)
            {
                var l = new List<V2>();
                for (int j = 0; j < count; j++)
                {
                    double h = h0 + j * pas;
                    V2 c = foot + u * h;
                    bool ok = h + r <= H - 1
                           && Side(c, seamM, seamF, flapMid) >= r + 1     // recouvert une fois roulé
                           && Side(c, flapM, flapF, seamMid) >= r + 1     // matière jusqu'au bout de patte
                           && DBas(c) >= r + 1 && DHaut(c) >= r + 1;      // matière jusqu'aux bords haut / bas
                    if (!ok) break;
                    l.Add(c);
                }
                return l;
            }

            // Le NOMBRE de trous est le code : si la pièce est trop basse pour l'entraxe saisi,
            // on resserre (pas de 1 mm) jusqu'à Ø + 3 mm de matière avant de déclarer le code faux.
            var best = Pose(p.TrouPas);
            double pasU = p.TrouPas;
            for (double pas = p.TrouPas - 1; best.Count < count && pas >= p.TrouDiam + 3 - 1e-9; pas -= 1)
            {
                var t = Pose(pas);
                if (t.Count > best.Count) { best = t; pasU = pas; }
            }
            outC.AddRange(best);
            if (best.Count < count)
                msg = $"{best.Count}/{count} trous seulement : CODE FAUX (+ de recouvrement ou - d'entraxe)";
            else if (pasU < p.TrouPas - 1e-9)
                msg = (msg != null ? msg + " ; " : "") + $"entraxe resserré à {pasU:0} mm pour loger les {count} trous";
            return msg;
        }

        // Point du développé -> (θ de sa génératrice, distance au bord bas le long de celle-ci).
        // Les génératrices sont des droites en éventail : bissection sur le côté où tombe le point.
        // Le développé étant isométrique, la distance à plat = la distance sur la tôle roulée.
        static V2 ToCone(Func<double, V2> fT, Func<double, V2> fB, double lo, double hi, V2 c)
        {
            double Side(double t) { V2 a = fB(t); return V2.Cross(fT(t) - a, c - a); }
            double slo = Side(lo);
            for (int it = 0; it < 60; it++)
            {
                double mid = 0.5 * (lo + hi), sm = Side(mid);
                if (Math.Sign(sm) == Math.Sign(slo)) { lo = mid; slo = sm; } else hi = mid;
            }
            double tc = 0.5 * (lo + hi);
            V2 a0 = fB(tc), d = (fT(tc) - a0).Unit();
            return new V2(tc, V2.Dot(c - a0, d));
        }

        // Échantillonne une arête développée (courbe exacte + tangentes exactes) et la
        // convertit en PEU d'arcs LONGS tangents (ArcFit.FitLong) : aucun arc < arc mini.
        // Ajoute les sommets de départ ; le point final est porté par le sommet suivant du contour.
        static ArcFit.Stats AppendCurve(List<BVertex> outV, Func<double, V2> f, Func<double, V2> tf,
                                        double t0, double t1, double tol, double tolMax, double arcMini)
        {
            if (Math.Abs(t1 - t0) < 1e-12) return new ArcFit.Stats { MinLen = double.PositiveInfinity, UsedMin = arcMini, UsedTol = tol };
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
            var loc = ArcFit.FitLong(P, T, tol, tolMax, arcMini, out var st);
            outV.AddRange(loc);
            return st;
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

        /// <summary>Modèle 3D assemblé du coude (tube conique mitré). Avec les développés (gores),
        /// les trous repère sont posés en 3D. En coude contre coude : + le 2e coude, tourné de 180°
        /// autour de son axe et emboîté sur le mâle du 1er (dévoiement en Z).</summary>
        public static List<Band3D> BuildElbow3D(Params p, List<Gore>? gores = null)
        {
            var specs = Specs(p, out _, out double alpha, out double phi);
            int N = specs.Count;
            double seam = SeamAng(p);
            var bands = new List<Band3D>();

            // axe : orientation de chaque virole (le S revient dans l'autre sens après l'élément central)
            var u = new V3[N]; var h = new V3[N]; var P = new V3[N + 1];
            for (int k = 0; k < N; k++)
            {
                double an = specs[k].Ang;
                u[k] = new V3(Math.Cos(an), 0, -Math.Sin(an));
                h[k] = new V3(Math.Sin(an), 0,  Math.Cos(an));
            }
            P[0] = new V3(0, 0, 0);
            for (int k = 0; k < N; k++) P[k + 1] = P[k] + u[k] * specs[k].L;

            for (int k = 0; k < N; k++)
            {
                var sp = specs[k];
                var (F, M) = Gore3D(sp, alpha, p.NdivDev, seam);
                V3 C = (P[k] + P[k + 1]) * 0.5;
                V3 Rot(V3 v) => h[k] * v.X + new V3(0, v.Y, 0) + (u[k] * (-v.Z));    // x->h, y->y, z->-u
                V3 Apply(V3 v) => Rot(v) + C;
                var Fg = new V3[F.Length]; var Mg = new V3[M.Length];
                for (int i = 0; i < F.Length; i++) { Fg[i] = Apply(F[i]); Mg[i] = Apply(M[i]); }

                // normale extérieure du cône ρ = m·z + b : (cos θ, sin θ, −m)
                double m = (sp.Rf - sp.r) / sp.L;
                V3 Nrm(double th) { var n = new V3(Math.Cos(th), Math.Sin(th), -m); return n * (1 / n.Norm()); }
                var band = new Band3D { Fem = Fg, Mal = Mg, SeamN = Rot(Nrm(seam)) };

                // trous repère : (θ depuis la couture, distance au bord bas) -> point de la génératrice,
                // 1 mm au-dessus (la patte est par-dessus)
                if (gores != null && k < gores.Count && gores[k].TrousTS.Count > 0)
                {
                    var tl = new List<V3>(); var tn = new List<V3>();
                    foreach (var ts in gores[k].TrousTS)
                    {
                        double thG = ts.X + seam;
                        V3 B = Pt3(sp, alpha, thG, false), T = Pt3(sp, alpha, thG, true);
                        V3 d = T - B, n = Nrm(thG);
                        tl.Add(Apply(B + d * (ts.Y / d.Norm()) + n * 1.0));
                        tn.Add(Rot(n));
                    }
                    band.Trous = tl.ToArray(); band.TrousN = tn.ToArray();
                }
                bands.Add(band);
            }

            if (p.CoudeContreCoude && !p.ZUnePiece)
            {
                // Coude 2 = coude 1 tourné de 180° autour de son axe d'entrée (X), puis orienté
                // sur la sortie du coude 1 (rotation de la déviation dans le plan XZ) et
                // emboîté sur son mâle (recul de l'emboîtement le long du joint).
                double be = (N - 1) * phi, cb = Math.Cos(be), sb = Math.Sin(be);
                V3 Rz(V3 v) { double x = v.X, y = -v.Y, z = -v.Z; return new V3(x * cb + z * sb, y, -x * sb + z * cb); }
                V3 O2 = P[N] - u[N - 1] * p.EmboitementZ;
                V3[] Map(V3[] a) { var r = new V3[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = Rz(a[i]) + O2; return r; }
                V3[] MapN(V3[] a) { var r = new V3[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = Rz(a[i]); return r; }
                int n1 = bands.Count;
                for (int k = 0; k < n1; k++)
                {
                    var b = bands[k];
                    bands.Add(new Band3D { Fem = Map(b.Fem), Mal = Map(b.Mal), SeamN = Rz(b.SeamN),
                                           Trous = Map(b.Trous), TrousN = MapN(b.TrousN) });
                }
            }
            return bands;
        }

        /// <summary>Plus petit nombre d'éléments (jusqu'à 8) qui ramène l'écart aux onglets sous seuil.
        /// Renvoie (n, écart) ou (0, 0) si aucun ne convient.</summary>
        public static (int n, double ecart) ElementsPourEcart(Params p, double seuil)
        {
            for (int n = Math.Max(2, p.NbSegments) + 1; n <= 8; n++)
            {
                var q = p.Clone(); q.NbSegments = n;
                if (Validate(q) != null) continue;
                double e = EcartJoints(q, BuildElbow3D(q));
                if (e <= seuil) return (n, e);
            }
            return (0, 0);
        }

        /// <summary>Plus grand écart 3D entre les 2 bords qui se rejoignent à un onglet (mm).
        /// Nul pour un cylindre ; sur un tube conique, mitrer laisse un léger décalage qui grandit
        /// avec la réduction (rattrapé au pointage). Pas compté à l'emboîtement entre 2 coudes.</summary>
        public static double EcartJoints(Params p, List<Band3D> bands)
        {
            int per = p.CoudeContreCoude && !p.ZUnePiece ? bands.Count / 2 : bands.Count;
            double mx = 0;
            for (int k = 0; k + 1 < bands.Count; k++)
            {
                if (per > 0 && (k + 1) % per == 0) continue;           // joint entre les 2 coudes = emboîtement
                var A = bands[k].Mal; var B = bands[k + 1].Fem;
                foreach (var q in A)
                {
                    double best = double.PositiveInfinity;
                    for (int i = 0; i + 1 < B.Length; i++)
                    {
                        V3 a = B[i], ab = B[i + 1] - a;
                        double l2 = ab.X * ab.X + ab.Y * ab.Y + ab.Z * ab.Z;
                        V3 aq = q - a;
                        double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, (aq.X * ab.X + aq.Y * ab.Y + aq.Z * ab.Z) / l2));
                        best = Math.Min(best, (q - (a + ab * t)).Norm());
                    }
                    mx = Math.Max(mx, best);
                }
            }
            return mx;
        }
    }

    // ---------------------------------------------------------------------
    //  ArcFit : courbe -> chaîne d'arcs TANGENTS (G1).
    //
    //  FitLong (export DXF) : PEU d'arcs LONGS, AUCUN plus court que l'arc
    //  mini. Programmation dynamique sur des nœuds pris sur la courbe exacte
    //  (point + tangente exacte) : chaque saut nœud a -> nœud b est soit UN
    //  arc (tangent aux deux bouts à KinkTolDeg près), soit un biarc (2 arcs
    //  tangents entre eux). Un saut n'est accepté que si l'écart à la courbe
    //  est ≤ tol ET si chaque arc fait ≥ arc mini. On garde le chemin qui a le
    //  MOINS d'arcs, puis, à égalité, celui dont le plus court est le plus long.
    //  => fini les dizaines de bouts de 10 mm que Metalix ne sait pas grignoter.
    //
    //  Fit (ancien, secours) : subdivision récursive en biarcs tant que
    //  l'écart > tol. Ne sert plus que si FitLong échoue (jamais en pratique).
    // ---------------------------------------------------------------------
    public static class ArcFit
    {
        const double MaxBulge = 0.41421356237;   // tan(22,5°) => arcs ≤ 90° (ancien Fit)
        const double MaxBulgeLong = 0.70020753821; // tan(35°) => arcs ≤ 140° (FitLong)
        public const double KinkTolDeg = 0.1;    // cassure tolérée à un nœud pour un arc simple

        public struct Stats
        {
            public double MinLen;    // plus court arc produit (mm)
            public double MaxDev;    // écart maxi mesuré sur TOUS les échantillons (mm)
            public double UsedMin;   // arc mini réellement tenu (abaissé seulement si impossible)
            public double UsedTol;   // tolérance réellement utilisée (montée auto pour tenir l'arc mini)
            public int Arcs;
        }

        // Arc (ou droite) prêt pour les mesures d'écart : centre, rayon, sens.
        readonly struct ArcGeo
        {
            readonly V2 A, B, C; readonly double R; readonly int S; readonly bool Line;
            public readonly double Len;
            public ArcGeo(V2 a, V2 b, double bulge)
            {
                A = a; B = b;
                double c = (b - a).Norm();
                if (Math.Abs(bulge) < 1e-9) { Line = true; C = a; R = 0; S = 0; Len = c; return; }
                Line = false;
                Circle(a, b, bulge, out C, out R);
                S = bulge > 0 ? 1 : -1;
                double sw = Math.Abs(4 * Math.Atan(bulge));
                Len = R * sw;
            }
            // Distance d'un point à l'arc (secteur testé par produits vectoriels : arcs < 180°).
            public double Dist(V2 q)
            {
                if (Line) return DistSeg(q, A, B);
                V2 qa = A - C, qq = q - C, qb = B - C;
                if (S * V2.Cross(qa, qq) >= 0 && S * V2.Cross(qq, qb) >= 0)
                    return Math.Abs(qq.Norm() - R);
                return Math.Min((q - A).Norm(), (q - B).Norm());
            }
        }

        /// <summary>
        /// Courbe dense (P, T unitaires, pas ~0,5 mm) -> sommets à bulge du premier point à
        /// l'avant-dernier tronçon (le point final est porté par le sommet suivant du contour).
        /// Priorité à l'arc mini (= Ø du rond : en dessous Metalix saute l'arc) :
        ///   1) on essaie à tol ; si l'arc mini ne tient pas, la tolérance monte par paliers
        ///      (×1,25) jusqu'à tolMax ;
        ///   2) si même à tolMax c'est impossible (petit Ø très cintré), on garde tolMax et on
        ///      cherche le plus grand arc mini tenable (dichotomie). Valeurs rendues dans st.
        /// </summary>
        public static List<BVertex> FitLong(V2[] P, V2[] T, double tol, double tolMax, double minLen, out Stats st)
        {
            tol = Math.Max(0.005, tol);
            tolMax = Math.Max(tol, tolMax);
            minLen = Math.Max(0, minLen);
            int n = P.Length - 1;
            var res = new List<BVertex>();
            st = new Stats { MinLen = double.PositiveInfinity, UsedMin = minLen, UsedTol = tol };
            if (n < 1) return res;

            // longueur cumulée le long de la courbe
            var cum = new double[n + 1];
            for (int i = 1; i <= n; i++) cum[i] = cum[i - 1] + (P[i] - P[i - 1]).Norm();
            double len = cum[n];

            // nœuds : ~1 tous les 2 mm (≤ 300 nœuds) ; contrôle d'écart ~ tous les 1 mm
            double nodeStep = Math.Max(2.0, len / 300.0);
            int m = Math.Max(1, (int)Math.Ceiling(len / nodeStep));
            var idx = new int[m + 1];
            for (int k = 0, i = 0; k <= m; k++)
            {
                double target = len * k / m;
                while (i < n && cum[i] < target) i++;
                idx[k] = (k == 0) ? 0 : (k == m) ? n : Math.Max(idx[k - 1] + 1, Math.Min(n - (m - k), i));
            }
            double h = len / n;
            int stride = Math.Max(1, (int)Math.Round(nodeStep / 2 / Math.Max(1e-9, h)));

            // 1) arc mini tenu : tolérance montée par paliers si besoin
            double used = minLen, usedTol = tol;
            List<BVertex>? path = null;
            for (double t = tol; ; t = Math.Min(tolMax, t * 1.25))
            {
                path = Dp(P, T, cum, idx, stride, t, minLen);
                usedTol = t;
                if (path != null || t >= tolMax - 1e-12) break;
            }

            // 2) impossible même à tolMax : plus grand arc mini tenable (la faisabilité ne fait que
            //    croître quand l'arc mini baisse -> dichotomie, ~0,3 mm près)
            if (path == null)
            {
                double lo = 0, hi = minLen;
                List<BVertex>? best = Dp(P, T, cum, idx, stride, usedTol, 0);
                for (int it = 0; it < 7 && best != null; it++)
                {
                    double mid = (lo + hi) / 2;
                    var trial = Dp(P, T, cum, idx, stride, usedTol, mid);
                    if (trial != null) { lo = mid; best = trial; } else hi = mid;
                }
                path = best; used = lo;
            }
            if (path == null)
            {   // secours (ne devrait jamais servir) : ancien découpage récursif
                path = new List<BVertex>();
                Fit(P, T, usedTol, path);
                used = 0;
            }
            Merge(path, P[n]);

            // contrôle final sur TOUS les échantillons : écart réel + plus court arc
            st.UsedMin = used;
            st.UsedTol = usedTol;
            st.Arcs = path.Count;
            int seg = 0;
            double maxDev = 0, minArc = double.PositiveInfinity;
            var geos = new ArcGeo[path.Count];
            for (int k = 0; k < path.Count; k++)
            {
                V2 b = (k + 1 < path.Count) ? path[k + 1].P : P[n];
                geos[k] = new ArcGeo(path[k].P, b, path[k].Bulge);
                minArc = Math.Min(minArc, geos[k].Len);
            }
            for (int i = 0; i <= n; i++)
            {
                double d = double.PositiveInfinity;
                // le point i est près du tronçon courant ou des voisins : fenêtre glissante
                for (int k = Math.Max(0, seg - 1); k < Math.Min(path.Count, seg + 3); k++)
                {
                    double dk = geos[k].Dist(P[i]);
                    if (dk < d) { d = dk; if (k > seg) seg = k; }
                }
                if (d > maxDev) maxDev = d;
            }
            st.MaxDev = maxDev;
            st.MinLen = minArc;
            return path;
        }

        // Programmation dynamique : moins d'arcs d'abord, puis plus long "plus court arc".
        static List<BVertex>? Dp(V2[] P, V2[] T, double[] cum, int[] idx, int stride, double tol, double minLen)
        {
            int m = idx.Length - 1;
            var cnt = new int[m + 1]; var bot = new double[m + 1]; var from = new int[m + 1];
            var e1 = new BVertex[m + 1]; var e2 = new BVertex[m + 1]; var two = new bool[m + 1];
            for (int k = 0; k <= m; k++) { cnt[k] = int.MaxValue; bot[k] = 0; from[k] = -1; }
            cnt[0] = 0; bot[0] = double.PositiveInfinity;

            double kinkTol = KinkTolDeg * Math.PI / 180.0;
            double reachLim = 3 * tol;                        // au-delà, plus aucun saut ne tiendra
            double[] ratios = { 1.0, 0.75, 1.0 / 0.75, 0.55, 1.0 / 0.55, 0.4, 1.0 / 0.4, 0.28, 1.0 / 0.28, 0.2, 5.0 };

            // écart du biarc "d égaux" a -> b (infini si impossible) : mesure la portée depuis a
            double Reach(int ia, int ib, double lim)
            {
                if (!Biarc(P[ia], T[ia], P[ib], T[ib], 1.0, out V2 J, out double c1, out double c2)
                    || Math.Abs(c1) > MaxBulgeLong || Math.Abs(c2) > MaxBulgeLong) return double.PositiveInfinity;
                return Err2(P, ia, ib, stride, new ArcGeo(P[ia], J, c1), new ArcGeo(J, P[ib], c2), lim);
            }

            for (int a = 0; a < m; a++)
            {
                if (cnt[a] == int.MaxValue) continue;
                if (cnt[m] != int.MaxValue && cnt[a] + 1 > cnt[m]) continue;   // ne peut plus battre le meilleur
                int ia = idx[a];

                // 1) portée maxi depuis a (recherche exponentielle puis dichotomie)
                int good = a, badB = -1, step = 1;
                while (true)
                {
                    int b = Math.Min(m, a + step);
                    if (Reach(ia, idx[b], reachLim) <= reachLim) { good = b; if (b == m) break; step *= 2; }
                    else { badB = b; break; }
                }
                if (badB > 0)
                    while (badB - good > 1)
                    {
                        int mid = (good + badB) / 2;
                        if (Reach(ia, idx[mid], reachLim) <= reachLim) good = mid; else badB = mid;
                    }
                int bMax = Math.Max(a + 1, good);

                // 2) sauts candidats a -> b (seulement là où ça peut améliorer le nœud b)
                for (int b = a + 1; b <= bMax; b++)
                {
                    int ib = idx[b];
                    double span = cum[ib] - cum[ia];
                    if (span < minLen - 1e-9) continue;                        // même un arc seul serait trop court

                    bool canOne = cnt[a] + 1 < cnt[b] || (cnt[a] + 1 == cnt[b] && Math.Min(bot[a], span * 1.02) > bot[b] + 1e-9);
                    bool canTwo = span >= 2 * minLen - 1e-9 &&
                                  (cnt[a] + 2 < cnt[b] || (cnt[a] + 2 == cnt[b] && Math.Min(bot[a], span * 0.51) > bot[b] + 1e-9));
                    if (!canOne && !canTwo) continue;

                    // --- 1 seul arc, tangent au départ, qui retombe sur la tangente d'arrivée ---
                    if (canOne)
                    {
                        double bs = BulgeFromTangent(P[ia], T[ia], P[ib]);
                        if (Math.Abs(bs) <= MaxBulgeLong)
                        {
                            V2 et = EndTangent(P[ia], P[ib], bs);
                            double kink = Math.Abs(Math.Atan2(V2.Cross(et, T[ib]), V2.Dot(et, T[ib])));
                            if (kink <= kinkTol)
                            {
                                var g = new ArcGeo(P[ia], P[ib], bs);
                                if (g.Len >= minLen && Err1(P, ia, ib, stride, g, tol) <= tol)
                                {
                                    double nb = Math.Min(bot[a], g.Len);
                                    if (cnt[a] + 1 < cnt[b] || (cnt[a] + 1 == cnt[b] && nb > bot[b] + 1e-9))
                                    { cnt[b] = cnt[a] + 1; bot[b] = nb; from[b] = a; two[b] = false; e1[b] = new BVertex(P[ia], bs); }
                                    continue;   // un arc seul bat toujours un biarc sur le même saut
                                }
                            }
                        }
                    }

                    // --- biarc : plusieurs points de jonction essayés pour équilibrer les 2 arcs ---
                    if (!canTwo) continue;
                    double bestBot = -1; BVertex bv1 = default, bv2 = default;
                    foreach (double r in ratios)
                    {
                        if (!Biarc(P[ia], T[ia], P[ib], T[ib], r, out V2 J, out double c1, out double c2)
                            || Math.Abs(c1) > MaxBulgeLong || Math.Abs(c2) > MaxBulgeLong) continue;
                        var g1 = new ArcGeo(P[ia], J, c1); var g2 = new ArcGeo(J, P[ib], c2);
                        double lo = Math.Min(g1.Len, g2.Len);
                        if (lo < minLen || lo <= bestBot) continue;
                        if (Err2(P, ia, ib, stride, g1, g2, tol) > tol) continue;
                        bestBot = lo; bv1 = new BVertex(P[ia], c1); bv2 = new BVertex(J, c2);
                    }
                    if (bestBot >= 0)
                    {
                        double nb = Math.Min(bot[a], bestBot);
                        if (cnt[a] + 2 < cnt[b] || (cnt[a] + 2 == cnt[b] && nb > bot[b] + 1e-9))
                        { cnt[b] = cnt[a] + 2; bot[b] = nb; from[b] = a; two[b] = true; e1[b] = bv1; e2[b] = bv2; }
                    }
                }
            }
            if (cnt[m] == int.MaxValue) return null;

            var rev = new List<BVertex>();
            for (int k = m; k > 0; k = from[k])
            {
                if (two[k]) { rev.Add(e2[k]); rev.Add(e1[k]); }
                else rev.Add(e1[k]);
            }
            rev.Reverse();
            return rev;
        }

        // Écart maxi des échantillons (pas = stride) à 1 arc ; sort dès qu'on dépasse lim.
        static double Err1(V2[] P, int i0, int i1, int stride, in ArcGeo g, double lim)
        {
            double mx = 0;
            for (int i = i0 + stride; i < i1; i += stride)
            {
                double d = g.Dist(P[i]);
                if (d > mx) { mx = d; if (mx > lim) return mx; }
            }
            if (i1 - 1 > i0) mx = Math.Max(mx, g.Dist(P[i1 - 1]));
            return mx;
        }

        // Idem pour un biarc (distance = la plus petite aux 2 arcs).
        static double Err2(V2[] P, int i0, int i1, int stride, in ArcGeo g1, in ArcGeo g2, double lim)
        {
            double mx = 0;
            for (int i = i0 + stride; i < i1; i += stride)
            {
                double d = Math.Min(g1.Dist(P[i]), g2.Dist(P[i]));
                if (d > mx) { mx = d; if (mx > lim) return mx; }
            }
            if (i1 - 1 > i0) mx = Math.Max(mx, Math.Min(g1.Dist(P[i1 - 1]), g2.Dist(P[i1 - 1])));
            return mx;
        }

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
            => Biarc(p0, t0, p1, t1, 1.0, out J, out b1, out b2);

        // Biarc général : d1 = r·d0 (r = 1 -> "d égaux"). r déplace le point de jonction J
        // le long du bord : sert à équilibrer les 2 arcs pour qu'aucun ne soit trop court.
        static bool Biarc(V2 p0, V2 t0, V2 p1, V2 t1, double r, out V2 J, out double b1, out double b2)
        {
            J = p0; b1 = b2 = 0;
            V2 v = p1 - p0, w = t0 + t1 * r;
            double vv = V2.Dot(v, v), vw = V2.Dot(v, w), a = V2.Dot(w, w) - (1 + r) * (1 + r);   // a ≤ 0
            if (vv < 1e-18) return false;
            double disc = vw * vw - a * vv; if (disc < 0) return false;
            double den = vw + Math.Sqrt(disc);
            if (den < 1e-12) return false;
            double d = vv / den;                       // racine stable de a·d² - 2·vw·d + vv = 0
            if (!(d > 0) || double.IsInfinity(d)) return false;
            V2 q0 = p0 + t0 * d, q1 = p1 - t1 * (r * d);
            J = q0 + (q1 - q0) * (1.0 / (1 + r));
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
