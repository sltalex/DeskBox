using System.Collections.Generic;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace DeskBox.Services;

/// <summary>
/// Boundary rules for widget slide-out animations on multi-monitor setups.
/// Sliding an HWND fully past the current monitor's work-area edge lands it on
/// the adjacent display, where it stays fully visible until the animation
/// completes. When an adjacent display is detected beyond the slide edge, the
/// travel distance is confined so the widget stops flush with the current
/// monitor's boundary and fades out instead of leaving the screen. A subject
/// already hugging the boundary still travels a minimum distance (briefly
/// crossing onto the adjacent display while fading) so the slide stays
/// legible; the crossing sliver is covered by the accelerated edge fade.
/// </summary>
public static class WidgetSlideBoundaryPolicy
{
    private const double AdjacencyTolerancePx = 8.0;

    /// <summary>
    /// Minimum travel kept for subjects resting on (or nearly on) the
    /// boundary: sliding zero pixels would read as a plain fade. The value is
    /// capped by the unconfined offset so it never exceeds the original
    /// off-screen target.
    /// </summary>
    public const double MinConfinedSlidePx = 160.0;

    public readonly record struct SlideBoundaryDecision(double Offset, bool ConfineWithFade);

    /// <summary>
    /// Clamps a signed slide-out offset so the animating subject stays close
    /// to the current monitor's work-area edge instead of fully crossing onto
    /// an adjacent display. <paramref name="farEdge"/> is the subject edge
    /// that leads when sliding toward <paramref name="workAreaEdge"/>; for a
    /// rightward slide that is the right edge, for an upward slide the top
    /// edge, and so on. Subjects flush with the edge still slide at least
    /// <see cref="MinConfinedSlidePx"/> (capped by the unconfined offset),
    /// briefly crossing the boundary while the edge fade covers them.
    /// </summary>
    public static SlideBoundaryDecision ResolveSlideOffset(
        double unconfinedOffset,
        double farEdge,
        double workAreaEdge,
        bool hasAdjacentDisplay)
    {
        if (!hasAdjacentDisplay)
        {
            return new SlideBoundaryDecision(unconfinedOffset, ConfineWithFade: false);
        }

        double magnitude = Math.Abs(unconfinedOffset);
        double confined = Math.Abs(workAreaEdge - farEdge);
        double minSlide = Math.Min(MinConfinedSlidePx, magnitude);
        confined = Math.Clamp(confined, minSlide, magnitude);

        return new SlideBoundaryDecision(
            Math.Sign(unconfinedOffset) * Math.Max(0, confined),
            ConfineWithFade: true);
    }

    /// <summary>
    /// True when another display's outer bounds touch the current monitor's
    /// boundary on the slide side with at least some perpendicular overlap.
    /// Uses outer (physical) bounds: two abutting monitors share that boundary
    /// even when a taskbar pulls their work areas apart.
    /// </summary>
    public static bool HasAdjacentDisplayBeyondEdge(
        RectInt32 currentOuterBounds,
        string slideDirection)
    {
        int currentLeft = currentOuterBounds.X;
        int currentTop = currentOuterBounds.Y;
        int currentRight = currentOuterBounds.X + currentOuterBounds.Width;
        int currentBottom = currentOuterBounds.Y + currentOuterBounds.Height;

        try
        {
            // Index through the IReadOnlyList projection (IVectorView.GetAt):
            // foreach would take CsWinRT's enumerator path, which QIs the
            // vector for IIterable<DisplayArea> and throws
            // InvalidCastException.
            IReadOnlyList<DisplayArea> areas = DisplayArea.FindAll();
            for (int index = 0; index < areas.Count; index++)
            {
                RectInt32 bounds = areas[index].OuterBounds;
                if (bounds.X == currentOuterBounds.X &&
                    bounds.Y == currentOuterBounds.Y &&
                    bounds.Width == currentOuterBounds.Width &&
                    bounds.Height == currentOuterBounds.Height)
                {
                    continue;
                }

                int boundsLeft = bounds.X;
                int boundsTop = bounds.Y;
                int boundsRight = bounds.X + bounds.Width;
                int boundsBottom = bounds.Y + bounds.Height;

                bool overlapsVertically =
                    boundsBottom > currentTop &&
                    boundsTop < currentBottom;
                bool overlapsHorizontally =
                    boundsRight > currentLeft &&
                    boundsLeft < currentRight;

                bool adjacent = slideDirection switch
                {
                    SettingsService.WidgetAnimationSlideDirectionRight =>
                        overlapsVertically &&
                        Math.Abs(boundsLeft - currentRight) <= AdjacencyTolerancePx,
                    SettingsService.WidgetAnimationSlideDirectionLeft =>
                        overlapsVertically &&
                        Math.Abs(currentLeft - boundsRight) <= AdjacencyTolerancePx,
                    SettingsService.WidgetAnimationSlideDirectionUp =>
                        overlapsHorizontally &&
                        Math.Abs(currentTop - boundsBottom) <= AdjacencyTolerancePx,
                    SettingsService.WidgetAnimationSlideDirectionDown =>
                        overlapsHorizontally &&
                        Math.Abs(currentBottom - boundsTop) <= AdjacencyTolerancePx,
                    _ => false
                };

                if (adjacent)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            // A failed topology probe must never break the animation pipeline;
            // fall back to the unconfined slide (no adjacent display).
            App.Log($"[SlideBoundary] Display topology probe failed: {ex.Message}");
        }

        return false;
    }
}
