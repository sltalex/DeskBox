# Tree illustrations

Original project-local SVG artwork for the five tree-growth stages, using the repository license. No downloaded artwork or external resources are required.

- `stage-1.svg` through `stage-5.svg`: transparent 260 x 220 canvases, anchored at the same ground position. The final tree does not contain baked-in fruit.
- `ground.svg`: stationary grass-and-soil platform, shared by every stage.
- `fruit.svg`: 24 x 28 apple, overlaid according to the actual fruit inventory.

Lighting comes from the upper left. Only paths, ellipses, gradients and strokes are used; avoid SVG filters, embedded fonts, scripts and remote links to preserve WinUI SVG compatibility.

`TreeIllustrationLayout` defines the matching fruit slots and canopy hit target. Update it and its tests together if the mature crown changes. `TreeWidgetContent` moves the tree and fruit together, leaving the ground still, and stops motion when the view is hidden or system accessibility settings disable animations.
