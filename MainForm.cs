using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace CoudeDev
{
    public class MainForm : Form
    {
        readonly TableLayoutPanel root = new();
        readonly Panel inputs = new();
        readonly DBPanel preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20,24,31) };

        sealed class DBPanel : Panel { public DBPanel(){ DoubleBuffered = true; } }

        // === Thème standard TolTem ===
        static readonly Color ACCENT = Color.FromArgb(255,122,26);   // #FF7A1A orange accent
        static readonly Color PANEL  = Color.FromArgb(27,32,39);     // #1B2027 fond panneau gauche
        static readonly Color INPUT  = Color.FromArgb(38,44,53);     // #262C35 champs de saisie
        static readonly Color CANVAS = Color.FromArgb(20,24,31);     // #14181F zone de dessin
        static readonly Color FG     = Color.FromArgb(230,235,240);  // #E6EBF0 texte
        static readonly Color BTN    = Color.FromArgb(43,49,59);     // #2B313B bouton secondaire
        static readonly Color INK    = Color.FromArgb(18,21,27);     // texte sombre sur orange
        static readonly Color WARN   = Color.FromArgb(255,110,90);   // message d'erreur

        TextBox tDiam, tRed, tAngle, tNb, tRatio, tNdiv, tTrac, tRecF, tRecJ, tRecM;
        CheckBox chkAnnot, chkFourreau;
        Button btn2D, btn3D;
        Label lblInfo;
        List<Gore> _gores = new();
        List<Band3D> _bands3d = new();
        ElbowGeometry.Params _p = new();
        string? _err;

        bool _show3D = false;
        double _yaw = 0.6, _pitch = 1.05;     // vue 3/4 montrant le cintrage
        Point _lastMouse; bool _dragging;

        public MainForm()
        {
            // --- Base d'autoscale DPI ---
            // Form dessinee a 96 dpi (Segoe UI 9pt = 7x15). Le csproj est en DpiUnaware :
            // Windows rend a 100% puis agrandit la fenetre (lisible a 200%, leger flou).
            // A poser AVANT toute creation de controle.
            AutoScaleDimensions = new SizeF(7f, 15f);
            AutoScaleMode = AutoScaleMode.Font;

            SuspendLayout();
            Text = "Développé Coude Emboîtable Mâle/Femelle  —  Made with love by weapon pour TolTem";
            Width = 1180; Height = 830; StartPosition = FormStartPosition.CenterScreen;
            BackColor = PANEL; ForeColor = FG;
            Font = new Font("Segoe UI", 9);
            try {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var rn = Array.Find(asm.GetManifestResourceNames(),
                    n => n.EndsWith("toltem.ico", StringComparison.OrdinalIgnoreCase));
                if (rn != null) { using var st = asm.GetManifestResourceStream(rn); if (st != null) Icon = new Icon(st); }
            } catch { }

            root.Dock = DockStyle.Fill; root.ColumnCount = 2; root.RowCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);

            inputs.Dock = DockStyle.Fill; inputs.AutoScroll = true;
            root.Controls.Add(inputs, 0, 0);

            // --- Conteneur droite : barre de vue (haut) + zone de dessin (remplissage) ---
            var rightPanel = new Panel{ Dock = DockStyle.Fill, BackColor = CANVAS };
            preview.Dock = DockStyle.Fill;
            rightPanel.Controls.Add(preview);     // fill (ajouté en premier => au fond)

            var viewBar = new Panel{ Dock = DockStyle.Top, Height = 42, BackColor = CANVAS };
            btn2D = MakeSeg("Développé 2D", 8,  130);
            btn3D = MakeSeg("Vue 3D",      146, 110);
            btn2D.Click += (s,e)=>SetView(false);
            btn3D.Click += (s,e)=>SetView(true);
            viewBar.Controls.Add(btn2D);
            viewBar.Controls.Add(btn3D);
            rightPanel.Controls.Add(viewBar);     // top (ajouté après => réserve le haut)

            root.Controls.Add(rightPanel, 1, 0);
            preview.Paint += Preview_Paint;
            preview.Resize += (s,e)=>preview.Invalidate();
            preview.MouseDown += (s,e)=>{ if(_show3D){_dragging=true;_lastMouse=e.Location;} };
            preview.MouseUp   += (s,e)=>_dragging=false;
            preview.MouseMove += (s,e)=>{
                if(_dragging){ _yaw += (e.X-_lastMouse.X)*0.01; _pitch += (e.Y-_lastMouse.Y)*0.01;
                    _lastMouse=e.Location; preview.Invalidate(); } };

            int y = 12;
            void Header(string t){ var l=new Label{Text=t,Left=12,Top=y,Width=270,Font=new Font("Segoe UI",9,FontStyle.Bold),ForeColor=ACCENT}; inputs.Controls.Add(l); y+=24; }
            void Hint(string t){ var l=new Label{Text=t,Left=12,Top=y-6,Width=276,Height=18,ForeColor=Color.FromArgb(140,148,160),Font=new Font("Segoe UI",8f,FontStyle.Italic)}; inputs.Controls.Add(l); y+=16; }
            TextBox Field(string lbl,string val){
                var l=new Label{Text=lbl,Left=12,Top=y+3,Width=150,ForeColor=FG}; inputs.Controls.Add(l);
                var t=new TextBox{Left=168,Top=y,Width=110,Text=val,BackColor=INPUT,ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle};
                t.TextChanged+=(s,e)=>Recalc(); inputs.Controls.Add(t); y+=30; return t;
            }

            Header("GÉOMÉTRIE DU COUDE");
            tDiam  = Field("Ø voulu (femelle) mm", "200");
            tAngle = Field("Angle RÉEL du coude (°)", "90");
            Hint("180 = droit · 135 = coude à 45° · 90 = équerre");
            tNb    = Field("Nombre d'éléments", "2");
            tRatio = Field("Longueur bras ext. (mm)", "250");

            Header("EMBOÎTEMENT");
            chkFourreau = new CheckBox{ Text="Dév. femelle = barème fourreaux", Left=12, Top=y, Width=276, Height=22,
                ForeColor=FG, Checked=true };
            chkFourreau.CheckedChanged+=(s,e)=>Recalc();
            inputs.Controls.Add(chkFourreau); y+=28;
            Hint("Ø200 → 655 recouv. compris (comme ep-fourreaux)");
            tRed   = Field("Réduction mâle (mm circ)", "25");
            tRecF  = Field("Recouv. bout femelle (mm)", "20");
            tRecJ  = Field("Recouv. au joint (mm)", "25");
            tRecM  = Field("Recouv. bout mâle (mm)", "35");

            Header("DÉVELOPPÉ / DXF");
            tNdiv  = Field("Divisions développé", "160");
            tTrac  = Field("Nb génératrices tracées", "12");
            chkAnnot = new CheckBox{ Text="DXF : + traçage / repères / textes", Left=12, Top=y, Width=276, Height=22,
                ForeColor=FG, Checked=false };
            inputs.Controls.Add(chkAnnot); y+=28;
            Hint("décoché = contour DECOUPE seul (fichier machine)");

            y+=6;
            var btnGen=new Button{Text="⤓  Exporter les DXF (1 par pièce)",Left=12,Top=y,Width=276,Height=36,
                BackColor=ACCENT,ForeColor=INK,FlatStyle=FlatStyle.Flat,
                Font=new Font("Segoe UI",10.5f,FontStyle.Bold)};
            btnGen.FlatAppearance.BorderSize=0;
            btnGen.FlatAppearance.MouseOverBackColor=Color.FromArgb(255,140,55);
            btnGen.FlatAppearance.MouseDownBackColor=Color.FromArgb(225,100,15);
            btnGen.Click+=Generate; inputs.Controls.Add(btnGen);
            y+=46;

            lblInfo=new Label{Left=12,Top=y,Width=276,Height=200,ForeColor=FG,Font=new Font("Consolas",8.5f)};
            inputs.Controls.Add(lblInfo);

            var lblSign=new Label{Left=12,Top=y+204,Width=280,Height=20,Text=SIGN,
                ForeColor=Color.FromArgb(140,140,145),Font=new Font("Segoe UI",8.5f,FontStyle.Italic|FontStyle.Bold)};
            inputs.Controls.Add(lblSign);

            ResumeLayout(false);
            SetView(false);
            Recalc();
        }

        // Bouton de la barre segmentée (style pilule plate)
        Button MakeSeg(string txt, int left, int width)
        {
            var b = new Button{
                Text=txt, Left=left, Top=6, Width=width, Height=30,
                FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI",9f,FontStyle.Bold) };
            b.FlatAppearance.BorderSize=1;
            return b;
        }

        // Bascule 2D / 3D : l'onglet actif passe en orange plein
        void SetView(bool show3d)
        {
            _show3D = show3d;
            void Style(Button b, bool active){
                b.BackColor = active ? ACCENT : BTN;
                b.ForeColor = active ? INK : FG;
                b.FlatAppearance.BorderColor = active ? ACCENT : Color.FromArgb(70,78,90);
            }
            if (btn2D != null) Style(btn2D, !show3d);
            if (btn3D != null) Style(btn3D, show3d);
            preview.Invalidate();
        }

        bool TryD(TextBox t, out double v) =>
            double.TryParse(t.Text.Replace(',','.'), NumberStyles.Any, CultureInfo.InvariantCulture, out v);

        void Recalc()
        {
            try
            {
                if(!TryD(tDiam,out var d)||!TryD(tAngle,out var a)||!TryD(tRatio,out var r)||
                   !TryD(tRed,out var red)||!TryD(tRecF,out var rf)||!TryD(tRecJ,out var rj)||!TryD(tRecM,out var rm)) return;
                int nb=int.TryParse(tNb.Text,out var n)?Math.Max(2,n):2;
                int nd=int.TryParse(tNdiv.Text,out var x)?Math.Max(24,x):160;
                int tr=int.TryParse(tTrac.Text,out var tt)?Math.Max(2,tt):12;
                _p=new ElbowGeometry.Params{
                    Diametre=d, AngleReel=a, NbSegments=nb, LongueurBrasExt=r,
                    Reduction=red, NdivDev=nd, NbTracage=tr,
                    RecBoutFemelle=rf, RecJoint=rj, RecBoutMale=rm,
                    AlignFourreau=chkFourreau.Checked };

                _err = ElbowGeometry.Validate(_p);
                if (_err != null)
                {
                    _gores = new(); _bands3d = new();
                    lblInfo.ForeColor = WARN;
                    lblInfo.Text = "⚠ " + _err;
                    preview.Invalidate();
                    return;
                }

                _gores=ElbowGeometry.BuildElbow(_p);
                _bands3d=ElbowGeometry.BuildElbow3D(_p);

                double phi=(_p.Deviation*Math.PI/180.0)/(nb-1), alpha=phi/2;
                double cF=ElbowGeometry.CircFemelle(_p), cM=ElbowGeometry.CircMale(_p);
                BaremeFourreaux.Dev(d, out bool exact);
                string src=_p.AlignFourreau ? (exact?"barème ✔":"π·Ø+"+BaremeFourreaux.JointK(d).ToString("0.0")) : "π·Ø+recouv.";
                int amin=int.MaxValue, amax=0; double kink=0;
                foreach(var g in _gores){ amin=Math.Min(amin,g.ArcCount); amax=Math.Max(amax,g.ArcCount); kink=Math.Max(kink,g.MaxKinkDeg); }
                lblInfo.ForeColor = FG;
                lblInfo.Text =
                    $"Angle réel : {a:0.##}°  →  déviation {_p.Deviation:0.##}°\n"+
                    $"Onglet par joint : {alpha*180/Math.PI:0.##}°\n"+
                    $"Rayon cintrage (axe) : {ElbowGeometry.RayonAxe(_p):0} mm\n"+
                    $"Dév. femelle : {ElbowGeometry.DevFemelle(_p):0.0} mm ({src})\n"+
                    $"Dév. mâle    : {ElbowGeometry.DevMale(_p):0.0} mm\n"+
                    $"Ø réel F {cF/Math.PI:0.0}  →  Ø réel M {cM/Math.PI:0.0}\n"+
                    $"Circ. corps F {cF:0.0} · M {cM:0.0} mm\n"+
                    $"Recouv. : F {rf:0} → joint {rj:0} → M {rm:0} mm\n"+
                    $"Contour DXF : {(amin==amax?amin.ToString():amin+"–"+amax)} arcs/pièce\n"+
                    $"  tangents (cassure {kink:0.###}°), 4 coins\n"+
                    $"Export : {_gores.Count} DXF (P1 femelle → P{_gores.Count} mâle)"+
                    (_p.AlignFourreau && Math.Abs(rf-BaremeFourreaux.Recouvrement(d))>0.01
                        ? $"\n⚠ barème prévu avec recouv. F {BaremeFourreaux.Recouvrement(d):0} mm" : "");
                preview.Invalidate();
            }
            catch { }
        }

        void Preview_Paint(object? sender, PaintEventArgs e)
        {
            var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            g.Clear(CANVAS);
            if(_gores.Count==0)
            {
                if(_err!=null){
                    using var f=new Font("Segoe UI",11,FontStyle.Bold); using var b=new SolidBrush(WARN);
                    g.DrawString("⚠ "+_err,f,b,24,24);
                }
                return;
            }
            if(_show3D) Draw3D(g); else DrawFlat(g);
        }

        const string SIGN = "Made with love by weapon pour TolTem";

        void DrawFlat(Graphics g)
        {
            // bbox réelle d'un développé = contour de découpe (recouvrement INCLUS = encombrement tôle)
            (double mnx,double mxx,double mny,double mxy) RealBox(Gore go){
                double mnx=1e18,mxx=-1e18,mny=1e18,mxy=-1e18;
                foreach(var p in go.ContourPts){mnx=Math.Min(mnx,p.X);mxx=Math.Max(mxx,p.X);mny=Math.Min(mny,p.Y);mxy=Math.Max(mxy,p.Y);}
                return (mnx,mxx,mny,mxy);
            }

            var boxes=new List<(double mnx,double mxx,double mny,double mxy)>();
            double maxW=1, totH=0;
            foreach(var go in _gores){ var b=RealBox(go); boxes.Add(b);
                maxW=Math.Max(maxW,b.mxx-b.mnx); totH+=(b.mxy-b.mny); }

            // marges écran fixes pour loger titres + cotes
            const float LEFT=34, RIGHT=78, TOP=74, BOT=14, TITLEGAP=16, COTEGAP=34;
            int nEl=_gores.Count;
            double availW=preview.Width-LEFT-RIGHT;
            double availH=preview.Height-TOP-BOT-nEl*(TITLEGAP+COTEGAP);
            double sc=Math.Min(availW/maxW, availH/Math.Max(1,totH));
            if(sc<=0) sc=0.05;

            using var penCut=new Pen(Color.White,1.3f); using var penTrac=new Pen(Color.FromArgb(160,120,210),0.7f);
            using var penRep=new Pen(Color.FromArgb(90,160,255),1.5f);
            using var penSeam=new Pen(Color.FromArgb(80,200,120),1.2f){ DashStyle=DashStyle.Dash };
            using var br=new SolidBrush(Color.Gainsboro); using var fnt=new Font("Segoe UI",8);
            using var brRec=new SolidBrush(Color.FromArgb(120,210,150));
            using var fillRec=new SolidBrush(Color.FromArgb(45,80,200,120));
            using var brNode=new SolidBrush(Color.FromArgb(150,ACCENT));
            // cotation (cyan, calque COTATION ACI4)
            using var penDim=new Pen(Color.FromArgb(95,200,215),1f); using var brDim=new SolidBrush(Color.FromArgb(155,225,235));
            using var fntDim=new Font("Segoe UI",8f);

            void HDim(float x1,float x2,float yl,double mm){
                g.DrawLine(penDim,x1,yl,x2,yl);
                g.DrawLine(penDim,x1,yl-4,x1,yl+4); g.DrawLine(penDim,x2,yl-4,x2,yl+4);
                string s=$"{mm:0} mm"; var sz=g.MeasureString(s,fntDim);
                g.DrawString(s,fntDim,brDim,(x1+x2)/2-sz.Width/2,yl+2);
            }
            void VDim(float y1,float y2,float xl,double mm){
                g.DrawLine(penDim,xl,y1,xl,y2);
                g.DrawLine(penDim,xl-4,y1,xl+4,y1); g.DrawLine(penDim,xl-4,y2,xl+4,y2);
                string s=$"{mm:0} mm"; var sz=g.MeasureString(s,fntDim);
                var st=g.Save(); g.TranslateTransform(xl+6,(y1+y2)/2); g.RotateTransform(-90);
                g.DrawString(s,fntDim,brDim,-sz.Width/2,0); g.Restore(st);
            }

            double cy=TOP;
            for(int gi=0;gi<_gores.Count;gi++){
                var go=_gores[gi]; var b=boxes[gi];
                double w=b.mxx-b.mnx, h=b.mxy-b.mny;
                float top=(float)(cy+TITLEGAP);
                PointF P(V2 v)=>new PointF((float)(LEFT+(v.X-b.mnx)*sc),(float)(top+(b.mxy-v.Y)*sc));
                PointF[] PA(V2[] a){ var r=new PointF[a.Length]; for(int i=0;i<a.Length;i++) r[i]=P(a[i]); return r; }

                // recouvrement (patte) : zone verte + couture d'origine en pointillé
                if(go.Flap.Length>=3) g.FillPolygon(fillRec,PA(go.Flap));
                g.DrawLine(penSeam,P(go.SeamF),P(go.SeamM));
                g.DrawString($"{go.SeamRecF:0}",fnt,brRec,P(go.FlapF).X+3,P(go.FlapF).Y-2);
                g.DrawString($"{go.SeamRecM:0}",fnt,brRec,P(go.FlapM).X+3,P(go.FlapM).Y-12);

                // génératrices + repères talon / gorge
                int step=Math.Max(1,(go.Female.Length-1)/Math.Max(1,_p.NbTracage));
                for(int i=step;i<go.Female.Length-1;i+=step) g.DrawLine(penTrac,P(go.Female[i]),P(go.Male[i]));
                g.DrawLine(penRep,P(go.Female[go.HeelIndex]),P(go.Male[go.HeelIndex]));
                g.DrawLine(penRep,P(go.Female[go.ThroatIndex]),P(go.Male[go.ThroatIndex]));

                // contour de DECOUPE = exactement ce qui part au DXF (arcs tangents, patte incluse)
                g.DrawPolygon(penCut,PA(go.ContourPts));
                foreach(var v in go.Contour){ var q=P(v.P); g.FillEllipse(brNode,q.X-1.6f,q.Y-1.6f,3.2f,3.2f); }

                // titre + encombrement + superficie
                string dev = go.Tag=="FEMELLE" ? $"dév. bord femelle {go.DevHaut:0.#} mm    —    "
                           : go.Tag=="MALE"    ? $"dév. bord mâle {go.DevBas:0.#} mm    —    " : "";
                g.DrawString($"P{go.PartNo}  {go.Name}    —    {dev}encombr. {w:0} × {h:0} ({w*h/1e6:0.00} m²)    —    {go.ArcCount} arcs",
                    fnt,br,LEFT,(float)cy);

                // cotes extérieures (encombrement recouvrement inclus)
                float sx1=LEFT, sx2=(float)(LEFT+w*sc), sy1=top, sy2=(float)(top+h*sc);
                HDim(sx1,sx2,sy2+16,w);   // largeur dev (circonférence + lap) en bas
                VDim(sy1,sy2,sx2+18,h);   // hauteur dev à droite

                cy = top + h*sc + COTEGAP;
            }

            // Signature EN HAUT + légende (lignes séparées)
            using var fSign=new Font("Segoe UI",9,FontStyle.Italic|FontStyle.Bold);
            using var bSign=new SolidBrush(Color.FromArgb(150,150,165)); using var bGray=new SolidBrush(Color.Gray);
            g.DrawString(SIGN,fSign,bSign,20,6);
            g.DrawString("Blanc=DÉCOUPE (arcs tangents, points orange = jonctions)   Violet=TRAÇAGE   Bleu=REPÈRE   Vert=RECOUVREMENT   Cyan=COTES",
                fnt,bGray,20,26);
        }

        void Draw3D(Graphics g)
        {
            var bands=_bands3d;
            if(bands==null || bands.Count==0) return;
            int B=bands.Count;
            double cx=0,cy=0,cz=0; int cnt=0;
            foreach(var bd in bands){ foreach(var v in bd.Fem){cx+=v.X;cy+=v.Y;cz+=v.Z;cnt++;} foreach(var v in bd.Mal){cx+=v.X;cy+=v.Y;cz+=v.Z;cnt++;} }
            cx/=cnt;cy/=cnt;cz/=cnt;
            double cyaw=Math.Cos(_yaw),syaw=Math.Sin(_yaw),cp=Math.Cos(_pitch),sp=Math.Sin(_pitch);
            (double x,double y,double d) PR(V3 v){
                double x=v.X-cx,y=v.Y-cy,z=v.Z-cz;
                double x1=x*cyaw+z*syaw, z1=-x*syaw+z*cyaw;
                return (x1, y*cp - z1*sp, y*sp + z1*cp);
            }
            // projection unique de tous les points
            var Fp=new (double x,double y,double d)[B][];
            var Mp=new (double x,double y,double d)[B][];
            double minx=1e18,maxx=-1e18,miny=1e18,maxy=-1e18,mind=1e18,maxd=-1e18;
            for(int k=0;k<B;k++){
                var bd=bands[k]; int n=bd.Fem.Length;
                Fp[k]=new (double,double,double)[n]; Mp[k]=new (double,double,double)[n];
                for(int i=0;i<n;i++){
                    var pf=PR(bd.Fem[i]); var pm=PR(bd.Mal[i]); Fp[k][i]=pf; Mp[k][i]=pm;
                    minx=Math.Min(minx,Math.Min(pf.x,pm.x)); maxx=Math.Max(maxx,Math.Max(pf.x,pm.x));
                    miny=Math.Min(miny,Math.Min(pf.y,pm.y)); maxy=Math.Max(maxy,Math.Max(pf.y,pm.y));
                    mind=Math.Min(mind,Math.Min(pf.d,pm.d)); maxd=Math.Max(maxd,Math.Max(pf.d,pm.d));
                }
            }
            double sc=Math.Min((preview.Width-70)/Math.Max(1,maxx-minx),(preview.Height-90)/Math.Max(1,maxy-miny));
            double ox=preview.Width/2.0, oy=preview.Height/2.0+10, mcx=(minx+maxx)/2, mcy=(miny+maxy)/2;
            PointF SC(double x,double y)=>new PointF((float)(ox+(x-mcx)*sc),(float)(oy-(y-mcy)*sc));

            // 1) faces ombrées triées (peintre) — sous-échantillonnées
            var quads=new List<(double d,PointF[] pts)>();
            for(int k=0;k<B;k++){
                int n=Fp[k].Length, stp=Math.Max(1,(n-1)/80);
                for(int i=0;i<n-1;i+=stp){
                    int j=Math.Min(i+stp,n-1);
                    double d=(Fp[k][i].d+Fp[k][j].d+Mp[k][j].d+Mp[k][i].d)/4;
                    quads.Add((d,new[]{SC(Fp[k][i].x,Fp[k][i].y),SC(Fp[k][j].x,Fp[k][j].y),
                                       SC(Mp[k][j].x,Mp[k][j].y),SC(Mp[k][i].x,Mp[k][i].y)}));
                }
            }
            quads.Sort((a,b)=>a.d.CompareTo(b.d));
            using var edge=new Pen(Color.FromArgb(22,255,255,255),0.4f);
            using var fill=new SolidBrush(Color.Black);
            foreach(var q in quads){
                double t=(q.d-mind)/Math.Max(1e-6,maxd-mind), shf=0.20+0.55*t;
                fill.Color=Color.FromArgb((int)(shf*0.55*255),(int)(shf*0.62*255),(int)(shf*0.72*255));
                g.FillPolygon(fill,q.pts); g.DrawPolygon(edge,q.pts);
            }
            // 2) bagues par-dessus
            for(int k=0;k<B;k++){
                var fpts=new PointF[Fp[k].Length]; for(int i=0;i<fpts.Length;i++) fpts[i]=SC(Fp[k][i].x,Fp[k][i].y);
                var mpts=new PointF[Mp[k].Length]; for(int i=0;i<mpts.Length;i++) mpts[i]=SC(Mp[k][i].x,Mp[k][i].y);
                using(var pen=new Pen(k==0?Color.FromArgb(90,170,255):Color.FromArgb(160,160,170),1.6f)) g.DrawLines(pen,fpts);
                using(var pen=new Pen(k==B-1?Color.FromArgb(80,200,120):Color.FromArgb(160,160,170),1.6f)) g.DrawLines(pen,mpts);
            }
            // textes
            using var fSign=new Font("Segoe UI",9,FontStyle.Italic|FontStyle.Bold);
            using var bSign=new SolidBrush(Color.FromArgb(150,150,165));
            using var f=new Font("Segoe UI",9);
            using var bFem=new SolidBrush(Color.FromArgb(90,170,255)); using var bMal=new SolidBrush(Color.FromArgb(80,200,120));
            using var bAcc=new SolidBrush(ACCENT); using var bGray=new SolidBrush(Color.Gray);
            g.DrawString(SIGN,fSign,bSign,20,8);
            g.DrawString("● P1 FEMELLE (Ø exact)",f,bFem,20,28);
            g.DrawString("● P"+_gores.Count+" MÂLE (−"+_p.Reduction.ToString("0")+"mm circ)",f,bMal,190,28);
            g.DrawString($"Angle réel {_p.AngleReel:0.##}°  (déviation {_p.Deviation:0.##}°)",f,bAcc,20,48);
            g.DrawString("glisser pour tourner",f,bGray,20,preview.Height-22);
        }

        void Generate(object? sender, EventArgs e)
        {
            Recalc();
            if(_gores.Count==0){
                MessageBox.Show(_err ?? "Paramètres invalides.","CoudeDev",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return;
            }
            using var sfd=new SaveFileDialog{Filter="DXF (*.dxf)|*.dxf", OverwritePrompt=false,
                Title="Nom de base — 1 DXF par pièce (_P1_FEMELLE, _P2_MALE…)",
                FileName=$"Coude_{_p.AngleReel.ToString("0.#",CultureInfo.InvariantCulture)}deg_D{_p.Diametre:0}_{_p.NbSegments}el.dxf"};
            if(sfd.ShowDialog()!=DialogResult.OK)return;
            try{
                var paths=DxfWriter.PartPaths(sfd.FileName,_gores);
                var exist=paths.FindAll(File.Exists);
                if(exist.Count>0){
                    var names=new List<string>(); foreach(var x in exist) names.Add(Path.GetFileName(x));
                    if(MessageBox.Show("Ces fichiers existent déjà :\n\n"+string.Join("\n",names)+"\n\nLes remplacer ?",
                        "CoudeDev",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
                }
                var files=DxfWriter.WriteParts(sfd.FileName,_gores,_p,chkAnnot.Checked);
                var lst=new List<string>(); foreach(var x in files) lst.Add("  "+Path.GetFileName(x));
                MessageBox.Show($"{files.Count} DXF générés dans :\n{Path.GetDirectoryName(files[0])}\n\n"+string.Join("\n",lst)+
                    (chkAnnot.Checked?"\n\n(avec traçage / repères / textes)":"\n\n(contour DECOUPE seul)"),
                    "OK",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }catch(Exception ex){MessageBox.Show("Erreur : "+ex.Message);}
        }
    }
}
