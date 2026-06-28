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
        public double Norm()=>Math.Sqrt(X*X+Y*Y); }

    /// <summary>Développé d'une virole : bord femelle, bord mâle, recouvrement dégradé.</summary>
    public class Gore
    {
        public string Name = "";
        public int Qty = 1;
        public V2[] Female = Array.Empty<V2>();
        public V2[] Male   = Array.Empty<V2>();
        public double SeamRecF = 0;   // recouvrement côté bord femelle (mm)
        public double SeamRecM = 0;   // recouvrement côté bord mâle (mm)
        public int HeelIndex, ThroatIndex;
        public double Width;
    }

    /// <summary>Bande 3D assemblée (bague femelle + bague mâle dans le repère du coude).</summary>
    public class Band3D { public V3[] Fem = Array.Empty<V3>(); public V3[] Mal = Array.Empty<V3>(); }

    public static class ElbowGeometry
    {
        public class Params
        {
            public double Diametre = 200;
            public double Reduction = 25;     // mâle = circonférence - X mm (sur tout le coude)
            public double AngleTotal = 90;
            public int NbSegments = 2;
            public double LongueurBrasExt = 250;   // longueur extrados (extérieur) du bras, mm
            public int NdivDev = 160;
            public int NbTracage = 12;
            public double RecBoutFemelle = 20;   // recouvrement au bout femelle (mm)
            public double RecJoint       = 25;   // recouvrement au joint (mm)
            public double RecBoutMale    = 35;   // recouvrement au bout mâle (mm)
        }

        // Longueur extrados (heel, θ=0) du bout femelle pour un Rm donné.
        static double HeelFemelle(Params p, double Rm)
        {
            int N = Math.Max(2, p.NbSegments);
            double al = (p.AngleTotal * Math.PI / 180.0) / (N - 1) / 2.0;
            double Lend = Rm * Math.Tan(al), Lint = 2 * Rm * Math.Tan(al);
            double Ltot = 2 * Lend + (N - 2) * Lint;
            if (Ltot < 1e-6) return 0;
            double Rfem = p.Diametre / 2.0;
            double Rmal = p.Diametre / 2.0 - p.Reduction / (2 * Math.PI);
            double Rf = Rfem, r = Rfem + (Rmal - Rfem) * (Lend / Ltot);
            V3 F0 = SolveZ(Rf, r, Lend, 0,  0, true);    // haut droit (femelle)
            V3 M0 = SolveZ(Rf, r, Lend, al, 0, false);   // bas onglet, extrados
            return (F0 - M0).Norm();
        }

        // Trouve Rm pour que l'extrados du bras = LongueurBrasExt (bissection).
        static double SolveRm(Params p)
        {
            double al = (p.AngleTotal * Math.PI / 180.0) / (Math.Max(2, p.NbSegments) - 1) / 2.0;
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
            phi = (p.AngleTotal * Math.PI / 180.0) / (N - 1);
            alpha = phi / 2.0;
            double Rm = SolveRm(p);
            double Lend = Rm * Math.Tan(alpha);
            double Lint = 2 * Rm * Math.Tan(alpha);

            var lens = new double[N];
            for (int k = 0; k < N; k++) lens[k] = (k == 0 || k == N - 1) ? Lend : Lint;
            double total = 0; foreach (var l in lens) total += l;
            Ltot = total;

            double Rfem = p.Diametre / 2.0;
            double Rmal = p.Diametre / 2.0 - p.Reduction / (2 * Math.PI);
            double Rad(double s) => Rfem + (Rmal - Rfem) * (s / total);

            var s = new double[N + 1];
            for (int k = 0; k < N; k++) s[k + 1] = s[k] + lens[k];

            var specs = new List<Spec>();
            for (int k = 0; k < N; k++)
            {
                string name = (k == 0) ? $"BOUT FEMELLE  Ø{p.Diametre:0} exact"
                            : (k == N - 1) ? $"BOUT MALE  -{p.Reduction:0}mm circ (Ø{2*Rmal:0.#})"
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

        static V2 PlacePoint(V2 a, V2 b, double la, double lb)
        {
            V2 ab = b - a; double d = ab.Norm(); if (d < 1e-9) d = 1e-9;
            double x = (la * la - lb * lb + d * d) / (2 * d);
            double h2 = la * la - x * x; if (h2 < 0) h2 = 0;
            double h = Math.Sqrt(h2);
            V2 ex = ab * (1.0 / d), ey = new V2(-ex.Y, ex.X);
            return a + ex * x + ey * h;
        }

        static (V2[] F2, V2[] M2) Unfold(V3[] F, V3[] M)
        {
            int n = F.Length; var F2 = new V2[n]; var M2 = new V2[n];
            F2[0] = new V2(0, 0); M2[0] = new V2(0, -(F[0] - M[0]).Norm());
            for (int i = 1; i < n; i++)
            {
                F2[i] = PlacePoint(F2[i - 1], M2[i - 1], (F[i] - F[i - 1]).Norm(), (F[i] - M[i - 1]).Norm());
                M2[i] = PlacePoint(F2[i], M2[i - 1], (M[i] - F[i]).Norm(), (M[i] - M[i - 1]).Norm());
            }
            return (F2, M2);
        }

        // Redresse le développé : bord femelle horizontal (parallèle à l'écran).
        static void Normalize(V2[] F2, V2[] M2)
        {
            double ang = Math.Atan2(F2[F2.Length - 1].Y - F2[0].Y, F2[F2.Length - 1].X - F2[0].X);
            double ca = Math.Cos(-ang), sa = Math.Sin(-ang);
            void Rot(V2[] a){ for (int i = 0; i < a.Length; i++){ double x = a[i].X, y = a[i].Y;
                a[i] = new V2(x * ca - y * sa, x * sa + y * ca); } }
            Rot(F2); Rot(M2);
        }

        /// <summary>Développés à plat de toutes les viroles.</summary>
        public static List<Gore> BuildElbow(Params p)
        {
            var specs = Specs(p, out _, out double alpha, out _);

            var gores = new List<Gore>();
            int N = specs.Count, mid = 0;
            for (int k = 0; k < N; k++)
            {
                var sp = specs[k];
                var (F, M) = Gore3D(sp, alpha, p.NdivDev);
                var (F2, M2) = Unfold(F, M);
                Normalize(F2, M2);
                string nm = sp.Name;
                if (nm == "INTERMEDIAIRE") nm = $"INTERMEDIAIRE {++mid}";
                // Recouvrement dégradé : bout femelle 20→joint 25, bout mâle 25→35
                double recF, recM;
                if (k == 0)           { recF = p.RecBoutFemelle; recM = p.RecJoint;     } // bout femelle
                else if (k == N - 1)  { recF = p.RecJoint;       recM = p.RecBoutMale;  } // bout mâle
                else                  { recF = p.RecJoint;       recM = p.RecJoint;     } // intermédiaires
                gores.Add(new Gore {
                    Female = F2, Male = M2, Name = nm, Qty = 1,
                    HeelIndex = 0, ThroatIndex = p.NdivDev / 2,
                    Width = (F2[F2.Length - 1] - F2[0]).Norm(),
                    SeamRecF = recF, SeamRecM = recM
                });
            }
            return gores;
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
}
