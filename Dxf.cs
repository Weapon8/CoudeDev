using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CoudeDev
{
    /// <summary>
    /// Export DXF AC1015 (R2000), UN FICHIER PAR PIÈCE :
    ///   base_P1_FEMELLE.dxf, (base_P2_INTER1.dxf ...), base_PN_MALE.dxf
    /// Calque DECOUPE = un seul contour fermé continu (LWPOLYLINE à bulges = vrais arcs
    /// tangents), recouvrement inclus : c'est la "ligne continue" que Metalix / AutoPunch
    /// grignote d'un trait (même format que TronconiqueDeporte).
    /// Traçage / repères / textes : en option, sur leurs propres calques.
    /// </summary>
    public static class DxfWriter
    {
        // Conventions calques (standard tube / Metalix)
        const string L_DECOUPE = "DECOUPE";        const int ACI_DECOUPE = 7;   // blanc
        const string L_RECOUV  = "RECOUVREMENT";   const int ACI_RECOUV  = 3;   // vert
        const string L_TRACAGE = "TRACAGE";        const int ACI_TRACAGE = 6;   // violet
        const string L_REPERE  = "REPERE";         const int ACI_REPERE  = 5;   // bleu
        const string L_INFOS   = "INFOS";          const int ACI_INFOS   = 8;   // gris

        static readonly Regex PartSuffix = new(@"_P\d+_[A-Z0-9]+$", RegexOptions.IgnoreCase);

        /// <summary>Chemins des fichiers qui seront écrits à partir du nom choisi.</summary>
        public static List<string> PartPaths(string chosenPath, List<Gore> gores)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(chosenPath)) ?? ".";
            string bn = PartSuffix.Replace(Path.GetFileNameWithoutExtension(chosenPath), "");
            var l = new List<string>();
            foreach (var g in gores) l.Add(Path.Combine(dir, $"{bn}_P{g.PartNo}_{g.Tag}.dxf"));
            return l;
        }

        /// <summary>Écrit un DXF par pièce. Renvoie les chemins écrits.</summary>
        public static List<string> WriteParts(string chosenPath, List<Gore> gores, ElbowGeometry.Params p, bool annotations)
        {
            var paths = PartPaths(chosenPath, gores);
            for (int i = 0; i < gores.Count; i++)
                File.WriteAllText(paths[i], BuildPart(gores[i], p, gores.Count, annotations), new UTF8Encoding(false));
            return paths;
        }

        public static string BuildPart(Gore g, ElbowGeometry.Params p, int nParts, bool annotations)
            => new Writer().Build(g, p, nParts, annotations);

        // Textes DXF en ASCII pur (Ø -> %%c, ° -> %%d, accents retirés) : lisibles partout.
        static string Ascii(string s)
        {
            s = s.Replace("Ø", "%%c").Replace("°", "%%d").Replace("×", "x");
            var sb = new StringBuilder();
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
            {
                if (ch < 128) sb.Append(ch);
                else if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append('?');
            }
            return sb.ToString();
        }

        sealed class Writer
        {
            static readonly CultureInfo C = CultureInfo.InvariantCulture;
            readonly StringBuilder s = new();
            int handle = 0x100;
            string Hx() => (++handle).ToString("X", C);
            static string F(double v) => v.ToString("0.######", C);
            void P(int code, string val) { s.Append(code.ToString(C)).Append('\n').Append(val).Append('\n'); }
            void P(int code, double val) => P(code, F(val));
            void P(int code, int val) => P(code, val.ToString(C));

            void LayerRec(string name, int color)
            {
                P(0, "LAYER"); P(5, Hx()); P(100, "AcDbSymbolTableRecord"); P(100, "AcDbLayerTableRecord");
                P(2, name); P(70, 0); P(62, color); P(6, "CONTINUOUS");
            }
            void Line(string layer, V2 a, V2 b)
            {
                P(0, "LINE"); P(5, Hx()); P(100, "AcDbEntity"); P(8, layer); P(100, "AcDbLine");
                P(10, a.X); P(20, a.Y); P(30, 0.0); P(11, b.X); P(21, b.Y); P(31, 0.0);
            }
            // UN contour fermé continu : LWPOLYLINE avec bulges (chaque bulge = un vrai arc).
            void PolyBulge(string layer, List<BVertex> verts, V2 off, bool closed)
            {
                P(0, "LWPOLYLINE"); P(5, Hx()); P(100, "AcDbEntity"); P(8, layer); P(100, "AcDbPolyline");
                P(90, verts.Count); P(70, closed ? 1 : 0);
                foreach (var v in verts)
                {
                    P(10, v.P.X + off.X); P(20, v.P.Y + off.Y);
                    if (v.Bulge != 0.0) P(42, v.Bulge.ToString("0.##########", C));
                }
            }
            void Text(string layer, double x, double y, string txt, double h)
            {
                P(0, "TEXT"); P(5, Hx()); P(100, "AcDbEntity"); P(8, layer); P(100, "AcDbText");
                P(10, x); P(20, y); P(30, 0.0); P(40, h); P(1, Ascii(txt)); P(100, "AcDbText");
            }

            public string Build(Gore g, ElbowGeometry.Params p, int nParts, bool annot)
            {
                // ---- HEADER ----
                P(0, "SECTION"); P(2, "HEADER");
                P(9, "$ACADVER"); P(1, "AC1015");
                P(9, "$INSUNITS"); P(70, 4);
                P(9, "$HANDSEED"); P(5, "FFFFF");
                P(0, "ENDSEC");

                // ---- TABLES ----
                P(0, "SECTION"); P(2, "TABLES");
                P(0, "TABLE"); P(2, "LTYPE"); P(5, Hx()); P(100, "AcDbSymbolTable"); P(70, 1);
                P(0, "LTYPE"); P(5, Hx()); P(100, "AcDbSymbolTableRecord"); P(100, "AcDbLinetypeTableRecord");
                P(2, "CONTINUOUS"); P(70, 0); P(3, "Solid line"); P(72, 65); P(73, 0); P(40, 0.0);
                P(0, "ENDTAB");
                P(0, "TABLE"); P(2, "LAYER"); P(5, Hx()); P(100, "AcDbSymbolTable"); P(70, annot ? 5 : 1);
                LayerRec(L_DECOUPE, ACI_DECOUPE);
                if (annot)
                {
                    LayerRec(L_RECOUV, ACI_RECOUV); LayerRec(L_TRACAGE, ACI_TRACAGE);
                    LayerRec(L_REPERE, ACI_REPERE); LayerRec(L_INFOS, ACI_INFOS);
                }
                P(0, "ENDTAB");
                P(0, "ENDSEC");

                // ---- BLOCKS ----
                P(0, "SECTION"); P(2, "BLOCKS");
                foreach (var bn in new[] { "*Model_Space", "*Paper_Space" })
                {
                    P(0, "BLOCK"); P(5, Hx()); P(100, "AcDbEntity"); P(8, "0"); P(100, "AcDbBlockBegin");
                    P(2, bn); P(70, 0); P(10, 0.0); P(20, 0.0); P(30, 0.0); P(3, bn); P(1, "");
                    P(0, "ENDBLK"); P(5, Hx()); P(100, "AcDbEntity"); P(8, "0"); P(100, "AcDbBlockEnd");
                }
                P(0, "ENDSEC");

                // ---- ENTITIES ----
                P(0, "SECTION"); P(2, "ENTITIES");

                // pièce posée coin bas-gauche (encombrement découpe) en 0,0
                double minX = 1e18, minY = 1e18, maxX = -1e18, maxY = -1e18;
                foreach (var q in g.ContourPts)
                { minX = Math.Min(minX, q.X); minY = Math.Min(minY, q.Y); maxX = Math.Max(maxX, q.X); maxY = Math.Max(maxY, q.Y); }
                var off = new V2(-minX, -minY);
                V2 Tr(V2 v) => v + off;

                // --- Contour DECOUPE : un seul trait continu, patte de recouvrement comprise ---
                PolyBulge(L_DECOUPE, g.Contour, off, true);

                if (annot)
                {
                    // Couture d'origine = bord recouvert (ligne de pose), PAS une découpe
                    Line(L_RECOUV, Tr(g.SeamF), Tr(g.SeamM));
                    V2 fF = Tr(g.FlapF), fM = Tr(g.FlapM);
                    Text(L_INFOS, fF.X + 2, fF.Y - 6, $"{g.SeamRecF:0}", 4);
                    Text(L_INFOS, fM.X + 2, fM.Y + 2, $"{g.SeamRecM:0}", 4);

                    // Génératrices (traçage) — hors talon (= bord découpé) et hors gorge (= repère)
                    int n = g.Female.Length - 1;
                    int step = Math.Max(1, n / Math.Max(1, p.NbTracage));
                    for (int i = step; i < n; i += step)
                        if (i != g.ThroatIndex) Line(L_TRACAGE, Tr(g.Female[i]), Tr(g.Male[i]));

                    // Repères : talon (extrados) = bord de couture θ=0 (déjà découpé) ; gorge (intrados) tracée
                    Line(L_REPERE, Tr(g.Female[g.ThroatIndex]), Tr(g.Male[g.ThroatIndex]));
                    V2 tH = Tr(g.Female[g.HeelIndex]), tG = Tr(g.Female[g.ThroatIndex]);
                    Text(L_INFOS, tH.X + 2, tH.Y - 8, "TALON", 5);
                    Text(L_INFOS, tG.X + 2, tG.Y - 8, "GORGE", 5);

                    // Etiquette + cartouche sous la pièce
                    string dev = g.Tag == "FEMELLE" ? $"   (dev bord femelle {g.DevHaut:0.#} mm recouv. compris)"
                               : g.Tag == "MALE"    ? $"   (dev bord male {g.DevBas:0.#} mm recouv. compris)" : "";
                    Text(L_INFOS, 0, -12, $"P{g.PartNo}/{nParts}  {g.Name}   x{g.Qty}{dev}", 7);
                    Text(L_INFOS, 0, -24,
                        $"COUDE {p.AngleReel:0.#} deg reel (deviation {p.Deviation:0.#} deg) - {p.NbSegments} viroles - Ø{p.Diametre:0} - bras ext {p.LongueurBrasExt:0}mm - male -{p.Reduction:0}mm circ", 5);
                    Text(L_INFOS, 0, -32,
                        $"dev femelle {ElbowGeometry.DevFemelle(p):0.#} mm" + (p.AlignFourreau ? " (bareme fourreaux)" : " (pi x D + recouv.)") +
                        $" - dev male {ElbowGeometry.DevMale(p):0.#} mm", 5);
                    Text(L_INFOS, 0, -41, "Made with love by weapon pour TolTem", 4);
                }

                P(0, "ENDSEC"); P(0, "EOF");
                return s.ToString();
            }
        }
    }
}
