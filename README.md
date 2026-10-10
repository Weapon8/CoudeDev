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

## Contour machine : une ligne continue (pas de pas-à-pas)
Le calque **DECOUPE** contient **un seul contour fermé** (LWPOLYLINE à bulges) :
- bords courbes convertis en **chaîne d'arcs tangents** (biarcs G1) calés sur la courbe
  exacte, écart ≤ 0,02 mm, **aucune cassure de tangente** entre arcs ;
- seuls 4 vrais coins (les 4 angles de la pièce) ;
- bord droit d'une extrémité = 1 seul arc (cône) ou 1 seule droite (cylindre).
L'AutoPunch / Metalix grignote donc chaque bord d'un trait au lieu de traiter des
centaines de petits segments en pas-à-pas (même format que TronconiqueDeporte).

## Recouvrement dégradé (pointage électro), intégré au contour
La couture longitudinale court sur toute la hauteur du tube conique : l'agrafe passe
linéairement de **recouvrement femelle** (grand Ø) à **recouvrement mâle** (petit Ø).
Au joint, valeur intermédiaire selon la station axiale.
La patte est **dans le contour DECOUPE** : ses bords haut et bas **prolongent la vraie
courbe** du bord (une fois roulée, elle tombe pile sur le début de la virole, sans
dépasser de l'onglet). La couture d'origine (bord recouvert) n'est qu'une ligne de
pose sur le calque RECOUVREMENT (option).

## Pièces générées — 1 fichier DXF par pièce
Le nom choisi à l'export sert de base :
1. `base_P1_FEMELLE.dxf` — **BOUT FEMELLE**, bord droit = développé barème fourreaux (ou πD), bord onglet au joint.
2. `base_P2_INTER1.dxf` … — **INTERMEDIAIRE n** ×(N−2), deux bords onglet (chacune unique).
3. `base_PN_MALE.dxf` — **BOUT MÂLE**, bord onglet au joint, bord droit réduit (corps femelle − X).

En 2 éléments : `P1_FEMELLE` + `P2_MALE`. Chaque pièce est posée coin bas-gauche en 0,0.

## Calques DXF (convention tube / Metalix)
Par défaut le fichier ne contient **que DECOUPE** (fichier machine propre).
Case « DXF : + traçage / repères / textes » pour ajouter les autres calques :

| Calque        | ACI | Usage                                           |
|---------------|-----|-------------------------------------------------|
| DECOUPE       | 7   | contour de découpe continu (arcs tangents)      |
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
