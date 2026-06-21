# CoudeDev — Développé de coude emboîtable mâle/femelle

Outil WinForms (.NET 8) générant le développé DXF d'un coude à onglet, conçu comme
un **tube légèrement conique** : extrémité **femelle = Ø voulu**, extrémité **mâle =
circonférence − X mm** (emboîtement). Diamètre, angle, nombre d'éléments et
recouvrements ajustables. Aperçu **Vue 3D** (rotation souris) **/ Vue à plat**.

## Modèle géométrique
- Coude à onglet, corps **conique continu** : le Ø décroît régulièrement de la femelle
  (πD) vers le mâle (πD − réduction). Tous les joints ont donc le même Ø des deux côtés.
- Onglet par joint : `α = angle / (2·(N−1))` (45° pour un 90° en 2 éléments).
- Chaque virole = tronçon de ce cône, coupé en onglet, développé par **triangulation**
  (déroulé isométrique exact). Validé : cas cylindre = sinusoïde exacte, largeur = πD,
  amplitude = (D/2)·tan α ; conicité = circonférences femelle/mâle exactes.
- NB : mitrer un tube conique laisse un très léger décalage aux joints (~1 % de D),
  inhérent et rattrapé au pointage/recouvrement. Chaque développé à plat reste exact.

## Recouvrement dégradé (pointage électro)
La couture longitudinale court sur toute la hauteur du tube conique : l'agrafe passe
linéairement de **recouvrement femelle** (grand Ø) à **recouvrement mâle** (petit Ø).
Au joint, valeur intermédiaire selon la station axiale. Calque RECOUVREMENT, largeur
variable F→M, cotée aux deux bouts.

## Pièces générées
1. **BOUT FEMELLE** — bord droit = Ø exact (πD), bord onglet au joint.
2. **INTERMEDIAIRE n** ×(N−2) — deux bords onglet (chacune unique, Ø propre).
3. **BOUT MÂLE** — bord onglet au joint, bord droit réduit (πD − X).

## Calques DXF (convention tube / Metalix)
| Calque        | ACI | Usage                                   |
|---------------|-----|-----------------------------------------|
| DECOUPE       | 7   | contour de découpe                      |
| RECOUVREMENT  | 3   | agrafe / recouvrement (dégradé)         |
| TRACAGE       | 6   | génératrices                            |
| REPERE        | 5   | repères talon (extrados) / gorge (intrados) |
| INFOS         | 8   | textes, cotes recouvrement              |

## Build (exe self-contained mono-fichier)
```
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
Sortie : `bin/Release/net8.0-windows/win-x64/publish/CoudeDev.exe`
