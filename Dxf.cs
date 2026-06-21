using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CoudeDev
{
    public static class DxfWriter
    {
        // Conventions calques (standard tube / Metalix)
        const string L_DECOUPE = "DECOUPE";        const int ACI_DECOUPE = 7;   // blanc
        const string L_RECOUV  = "RECOUVREMENT";   const int ACI_RECOUV  = 3;   // vert
        const string L_TRACAGE = "TRACAGE";        const int ACI_TRACAGE = 6;   // violet
        const string L_REPERE  = "REPERE";         const int ACI_REPERE  = 5;   // bleu
        const string L_INFOS   = "INFOS";          const int ACI_INFOS   = 8;   // gris

        static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        public static void Write(string path, List<Gore> gores, ElbowGeometry.Params p)
        {
            var sb = new StringBuilder();
            Header(sb);
            Tables(sb);
            sb.Append("0\nSECTION\n2\nENTITIES\n");

            double cursorY = 0;
            double gap = p.Diametre * 0.6 + 40;

            // Cartouche infos en tête
            Text(sb, L_INFOS, ACI_INFOS, 0, cursorY + 25,
                $"COUDE {p.AngleTotal:0} deg  -  {p.NbSegments} viroles  -  Ø{p.Diametre:0}  -  bras ext {p.LongueurBrasExt:0}mm  -  male -{p.Reduction:0}mm circ", 8);
            Text(sb, L_INFOS, ACI_INFOS, 0, cursorY + 12, "Made with love by weapon pour TolTem", 5);
            cursorY -= 10;

            foreach (var g in gores)
            {
                // bbox
                double minX=1e18,maxX=-1e18,minY=1e18,maxY=-1e18;
                foreach (var pt in g.Female){ minX=Math.Min(minX,pt.X); maxX=Math.Max(maxX,pt.X); minY=Math.Min(minY,pt.Y); maxY=Math.Max(maxY,pt.Y);}
                foreach (var pt in g.Male)  { minX=Math.Min(minX,pt.X); maxX=Math.Max(maxX,pt.X); minY=Math.Min(minY,pt.Y); maxY=Math.Max(maxY,pt.Y);}
                double h = maxY - minY;
                double oy = cursorY - maxY;     // place haut de la pièce sous le curseur
                double ox = -minX;

                V2 Tr(V2 v) => new V2(v.X + ox, v.Y + oy);

                // --- Contour DECOUPE ---
                var outline = new List<V2>();
                for (int i = 0; i < g.Female.Length; i++) outline.Add(Tr(g.Female[i]));
                for (int i = g.Male.Length - 1; i >= 0; i--) outline.Add(Tr(g.Male[i]));
                Polyline(sb, L_DECOUPE, ACI_DECOUPE, outline, true);

                // --- Génératrices TRACAGE ---
                int step = Math.Max(1, (g.Female.Length - 1) / Math.Max(1, p.NbTracage));
                for (int i = step; i < g.Female.Length - 1; i += step)
                    Line(sb, L_TRACAGE, ACI_TRACAGE, Tr(g.Female[i]), Tr(g.Male[i]));

                // --- Repères talon (extrados) & gorge (intrados) ---
                Line(sb, L_REPERE, ACI_REPERE, Tr(g.Female[g.HeelIndex]),   Tr(g.Male[g.HeelIndex]));
                Line(sb, L_REPERE, ACI_REPERE, Tr(g.Female[g.ThroatIndex]), Tr(g.Male[g.ThroatIndex]));
                Text(sb, L_INFOS, ACI_INFOS, Tr(g.Female[g.HeelIndex]).X+1, Tr(g.Female[g.HeelIndex]).Y+3, "TALON", 5);
                Text(sb, L_INFOS, ACI_INFOS, Tr(g.Female[g.ThroatIndex]).X+1, Tr(g.Female[g.ThroatIndex]).Y+3, "GORGE", 5);

                // --- Recouvrement dégradé (agrafe couture, pointage électro) ---
                if (g.SeamRecF > 0.1 || g.SeamRecM > 0.1)
                {
                    V2 fEnd = Tr(g.Female[g.Female.Length-1]);
                    V2 mEnd = Tr(g.Male[g.Male.Length-1]);
                    V2 fPrev= Tr(g.Female[g.Female.Length-2]);
                    V2 mPrev= Tr(g.Male[g.Male.Length-2]);
                    V2 tF = Norm(fEnd - fPrev); V2 tM = Norm(mEnd - mPrev);
                    V2 fOff = fEnd + tF * g.SeamRecF;   // largeur côté femelle
                    V2 mOff = mEnd + tM * g.SeamRecM;   // largeur côté mâle
                    Polyline(sb, L_RECOUV, ACI_RECOUV, new List<V2>{ fEnd, fOff, mOff, mEnd }, true);
                    Text(sb, L_INFOS, ACI_INFOS, fOff.X+2, fOff.Y, $"{g.SeamRecF:0}", 4);
                    Text(sb, L_INFOS, ACI_INFOS, mOff.X+2, mOff.Y, $"{g.SeamRecM:0}", 4);
                }

                // --- Etiquette ---
                Text(sb, L_INFOS, ACI_INFOS, ox + minX, oy + minY - 12,
                     $"{g.Name}   x{g.Qty}   (dev {g.Width:0.#}mm)", 7);

                cursorY = oy + minY - gap;
            }

            sb.Append("0\nENDSEC\n0\nEOF\n");
            File.WriteAllText(path, sb.ToString());
        }

        static V2 Norm(V2 v){ double n=v.Norm(); return n<1e-9? new V2(0,0): v*(1.0/n); }

        static void Header(StringBuilder sb)
        {
            sb.Append("0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1015\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n");
        }

        static void Tables(StringBuilder sb)
        {
            sb.Append("0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n70\n5\n");
            AddLayer(sb, L_DECOUPE, ACI_DECOUPE);
            AddLayer(sb, L_RECOUV,  ACI_RECOUV);
            AddLayer(sb, L_TRACAGE, ACI_TRACAGE);
            AddLayer(sb, L_REPERE,  ACI_REPERE);
            AddLayer(sb, L_INFOS,   ACI_INFOS);
            sb.Append("0\nENDTAB\n0\nENDSEC\n");
        }

        static void AddLayer(StringBuilder sb, string name, int aci)
        {
            sb.Append($"0\nLAYER\n2\n{name}\n70\n0\n62\n{aci}\n6\nCONTINUOUS\n");
        }

        static void Polyline(StringBuilder sb, string layer, int aci, List<V2> pts, bool closed)
        {
            sb.Append($"0\nLWPOLYLINE\n8\n{layer}\n62\n{aci}\n90\n{pts.Count}\n70\n{(closed?1:0)}\n");
            foreach (var p in pts) sb.Append($"10\n{F(p.X)}\n20\n{F(p.Y)}\n");
        }

        static void Line(StringBuilder sb, string layer, int aci, V2 a, V2 b)
        {
            sb.Append($"0\nLINE\n8\n{layer}\n62\n{aci}\n10\n{F(a.X)}\n20\n{F(a.Y)}\n30\n0\n11\n{F(b.X)}\n21\n{F(b.Y)}\n31\n0\n");
        }

        static void Text(StringBuilder sb, string layer, int aci, double x, double y, string txt, double height)
        {
            sb.Append($"0\nTEXT\n8\n{layer}\n62\n{aci}\n10\n{F(x)}\n20\n{F(y)}\n30\n0\n40\n{F(height)}\n1\n{txt}\n");
        }
    }
}
