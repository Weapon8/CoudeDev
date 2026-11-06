# CoudeDev — Développé de coude emboîtable mâle/femelle

Outil WinForms (.NET 8) générant le développé DXF d'un coude à onglet, conçu comme
un **tube légèrement conique** : extrémité **femelle = Ø voulu**, extrémité **mâle =
circonférence − X mm** (emboîtement). Diamètre, angle, nombre d'éléments et
recouvrements ajustables. Aperçu **Vue 3D** (rotation souris) **/ Vue à plat**.

## Saisie de l'angle : angle RÉEL (v1.1)
L'angle saisi est l'**angle réel entre les deux bras**, celui qu'on relève à la fausse
équerre : **180° = tube droit**, **135° = coude « à 45° »**, **90° = équerre**.
La déviation de l'axe vaut `180 − angle réel` (affichée dans le panneau d'infos).
Avant la v1.1, la saisie était la déviation (45 saisi = coude réel de 135°).

## Développé aligné sur le barème fourreaux (v1.2)
Case « Dév. femelle = barème fourreaux » (cochée par défaut) : le **développé côté
femelle (grand côté), recouvrement compris**, reprend le barème atelier FOURREAUX
galva/zinc de `ep-fourreaux.html` — ex. **Ø200 → 655 mm**. Valeur exacte du tableau
si le Ø y figure, sinon `π·Ø + joint` (21,0 jusqu'à Ø85 · 26,2 jusqu'à Ø200 · 36,2 au-delà).
- corps femelle roulé = développé barème − recouvrement femelle (Ø200 : 655 − 20 = 635,
  soit Ø réel 202,1 : passe sur le conduit comme un fourreau) ;
- côté mâle = corps femelle − réduction X (Ø200 : 635 − 25 = 610, + 35 de recouvrement = 645) ;
- le barème est établi avec 15 mm de recouvrement jusqu'à Ø85, 20 mm jusqu'à Ø200 et
  30 mm au-delà : un avertissement s'affiche si le recouvrement femelle saisi diffère.
Décochée : circonférence femelle = π·Ø exact (comportement v1.0/v1.1).

## Modèle géométrique
- Coude à onglet, corps **conique continu** : la circonférence décroît régulièrement de
  la femelle vers le mâle (femelle − réduction). Tous les joints ont donc le même Ø des deux côtés.
- Onglet par joint : `α = déviation / (2·(N−1))` (45° pour une équerre en 2 éléments).
- Chaque virole = tronçon de ce cône, coupé en onglet, développé **exactement**
  (déroulé analytique du tronc de cône, formule bien conditionnée qui retombe sur le
  cylindre quand la réduction est nulle). Validé contre l'ancienne triangulation
  (écart ≤ 0,06 mm) ; circonférences femelle/mâle exactes.
- NB : mitrer un tube conique laisse un très léger décalage aux joints (~1 % de D),
  inhérent et rattrapé au pointage/recouvrement. Chaque développé à plat reste exact.
- Contrôles : angle réel < 180°, onglet ≤ 75°, gorge positive (le bras ext. mini
  est indiqué si la longueur saisie est trop courte).

## Contour machine : peu d'arcs longs, aucune petite section (v1.3.1)
Le calque **DECOUPE** contient **une seule entité** : un contour fermé (LWPOLYLINE à
bulges). Les sommets à l'intérieur ne sont que les jonctions entre arcs (une courbe de
coude n'est pas un cercle, elle ne tient pas en un seul arc) :
- bords courbes convertis en **PEU d'arcs LONGS tangents** (G1, cassure ≤ 0,1°) calés
  sur la courbe exacte, écart ≤ **Tolérance contour** (0,2 mm par défaut = dentelure
  Metalix de l'atelier) ;
- **aucun arc plus court que l'Arc mini** (20 mm par défaut = Ø du rond de grignotage).
  Priorité à l'arc mini : si la courbe est trop cintrée (petit Ø, gros onglet), la
  tolérance monte toute seule par paliers jusqu'à la **Tolérance maxi auto** (0,5 mm).
  Si même là l'arc mini ne tient pas (ex. Ø60 à 90° en 2 éléments : 18,6 mm), c'est
  signalé en orange : monter la tolérance maxi ou ajouter un élément ;
- rayon de courbure mini du contour ≥ 28 mm sur toute la gamme testée (Ø60–Ø300) :
  le rond de 20 passe partout, y compris dans les creux ;
- seuls 4 vrais coins (les 4 angles de la pièce) ;
- bord droit d'une extrémité = 1 seul arc (cône) ou 1 seule droite (cylindre).

Avant (v1.2, écart 0,02 mm) : ~60 arcs de 10 mm par pièce sur un Ø200 à 2 éléments,
trop courts pour Metalix (grignotage à reprendre arc par arc). Maintenant : 17–18 arcs,
le plus court à 39 mm. Le découpage est choisi par programmation dynamique : le moins
d'arcs possible, puis, à égalité, celui dont le plus court est le plus long.

## Recouvrement dégradé (pointage électro), intégré au contour
La couture longitudinale court sur toute la hauteur du tube conique : l'agrafe passe
linéairement de **recouvrement femelle** (grand Ø) à **recouvrement mâle** (petit Ø).
Au joint, valeur intermédiaire selon la station axiale.
La patte est **dans le contour DECOUPE** : ses bords haut et bas **prolongent la vraie
courbe** du bord (une fois roulée, elle tombe pile sur le début de la virole, sans
dépasser de l'onglet). La couture d'origine (bord recouvert) n'est qu'une ligne de
pose sur le calque RECOUVREMENT (option).

## Trous repère mâle / femelle (v1.4)
Une fois poinçonnée, la pièce perd tous ses traçages : les apprentis / intérims ne savent plus
quelle virole est la femelle, laquelle est le mâle, ni où est la patte. Pas de poinçon de
marquage à l'atelier → on code la pièce avec le **rond standard (Ø6)**, poinçonné avec le contour :
- **nombre de trous = n° de la pièce** : P1 FEMELLE = 1 trou, P2 = 2 trous … PN MÂLE = N trous
  (le mâle = celle qui a le plus de trous, intermédiaires onglet/onglet compris en 3 éléments et plus) ;
- trous **toujours dans la patte de recouvrement** → le côté troué = la patte ;
- trous **toujours côté MÂLE de la virole** (bord bas) → le bord près des trous va vers le mâle ;
- 1er trou à **10 mm du bout de patte et 10 mm du bord côté mâle**, suivants empilés vers le haut
  à **15 mm d'entraxe** sur une colonne parallèle au bout de patte (tous restent à 10 mm du bord,
  même quand la patte se rétrécit en montant, ex. mâle 35 → 25) ;
- une fois roulé, l'autre bord recouvre les trous (chaque trou est contrôlé ENTIÈREMENT au-delà
  de la couture d'origine, + 1 mm) : **pas de trou traversant**.

Ex. Ø200 / 90° / 2 éléments : P1 femelle = 1 trou dans la patte de 25 (côté onglet),
P2 mâle = 2 trous dans la patte de 35 (côté bout mâle).

Réglages : case « Trous repère dans la patte », Ø trou, distance aux bords, entraxe.
Si la patte est trop étroite pour 10 mm, le trou est recentré dans la patte (signalé) ; si un
trou ne tient pas (patte trop petite, pièce trop basse), c'est signalé en orange : **code faux**.
Dans le DXF : `CIRCLE` sur le calque **DECOUPE** → Metalix / AutoPunch y met le rond du même Ø
(il faut ce rond dans la tourelle).

## Coude contre coude — dévoiement en Z (v1.4)
Cadre « COUDE CONTRE COUDE » en bas du panneau : choix **Coude normal** / **Coude contre coude**.
Deux coudes **identiques** montés l'un contre l'autre (ex. dévoiement de descente EP sous
débord de toit) : le 2e est **tourné de 180°** autour de son axe et **emboîté** sur le mâle du 1er.
Les axes d'entrée et de sortie sont parallèles, décalés du **déport**.
- Saisies : **déport entre axes** (mm) et **emboîtement** du mâle dans la femelle (mm, 30 par défaut),
  + Ø, angle réel, nombre d'éléments comme d'habitude.
- Le **rayon de cintrage est calculé** pour tenir le déport (le bras ext. saisi est grisé) :
  `déport = 2·Rm·Σ(Lk/Rm)·sin(k·φ) − emboîtement·sin(déviation)` — linéaire en Rm, formule fermée.
  Affichés : bras ext. calculé, **hauteur** du dévoiement (entrée → sortie, le long de l'axe).
- Déport trop petit (gorge nulle) : erreur avec le **déport mini** pour cet angle
  (un angle réel plus ouvert donne un déport mini plus petit).
- Export : les **mêmes DXF** qu'un coude seul, quantité **×2** (même code de trous : P1 = 1 trou…).
- Montage : coutures des 2 coudes **à l'opposé** (180°). Vue 3D : les 2 coudes en Z, coutures en
  vert, trous repère en orange.

### En 2 parties ou en 3 parties (S d'une pièce)
Le cadre propose 3 choix : **Coude normal**, **En 2 parties : 2 coudes emboîtés** (ci-dessus),
**En 3 parties : S, élément central** (le libellé suit le nombre d'éléments : 2N−1 parties).
- Le mâle du coude 1 et la femelle du coude 2 **fusionnent en une virole centrale** de longueur
  2·Lend, à **onglets opposés** (talon du haut à 0°, talon du bas à 180°). À plat, ses deux bords
  ondulent dans le même sens : une **bande en vague de hauteur constante** (une intermédiaire
  normale, elle, est en tonneau). Ex. 2 éléments : P1 FEMELLE, P2 CENTRE, P3 MÂLE.
- **Cône continu** de la femelle au mâle sur tout le S, **pas d'emboîtement** au milieu (champ grisé) ;
  même axe, donc même déport et même hauteur qu'en 2 coudes, l'emboîtement en moins.
- **Couture sur le côté** (fibre neutre, 90° du plan du coude) : elle reste alignée d'un bout à
  l'autre du S, comme sur un dévoiement du commerce. Talon et gorge sont tracés en REPÈRE (option).
- Chaque pièce ×1. Code des trous : P1 = 1 trou … P(2N−1) = 2N−1 trous ; le premier trou est à 10 mm
  du bord bas mesuré **perpendiculairement** (le bord est incliné quand la couture est sur le côté).
  Si la pièce est trop basse pour l'entraxe saisi, l'entraxe est **resserré** (jusqu'à Ø + 3) pour
  garder le bon nombre de trous ; sinon alerte « CODE FAUX ».
- Validé en cylindre : les bagues de chaque joint coïncident à 0,000 mm et la couture est une
  droite continue ; en conique, écart d'onglet habituel du cône (~1 % du Ø).

## Pièces générées — 1 fichier DXF par pièce
Le nom choisi à l'export sert de base :
1. `base_P1_FEMELLE.dxf` — **BOUT FEMELLE**, bord droit = développé barème fourreaux (ou πD), bord onglet au joint.
2. `base_P2_INTER1.dxf` … — **INTERMEDIAIRE n** ×(N−2), deux bords onglet (chacune unique).
3. `base_PN_MALE.dxf` — **BOUT MÂLE**, bord onglet au joint, bord droit réduit (corps femelle − X).

En 2 éléments : `P1_FEMELLE` + `P2_MALE`. Chaque pièce est posée coin bas-gauche en 0,0.

## Calques DXF (convention tube / Metalix)
Par défaut le fichier ne contient **que DECOUPE** (fichier machine propre : contour + trous repère).
Case « DXF : + traçage / repères / textes » pour ajouter les autres calques :

| Calque        | ACI | Usage                                           |
|---------------|-----|-------------------------------------------------|
| DECOUPE       | 7   | contour de découpe continu (arcs tangents) + trous repère (CIRCLE) |
| RECOUVREMENT  | 3   | couture d'origine = ligne de pose de la patte   |
| TRACAGE       | 6   | génératrices                                    |
| REPERE        | 5   | repère gorge (intrados) ; le talon = bord de couture |
| INFOS         | 8   | textes, valeurs de recouvrement, cartouche      |

## Build (exe self-contained mono-fichier)
`build.bat`, ou :
```
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
Sortie : `exe_final\CoudeDev.exe` (build.bat) ou `bin/Release/net8.0-windows/win-x64/publish/CoudeDev.exe`
