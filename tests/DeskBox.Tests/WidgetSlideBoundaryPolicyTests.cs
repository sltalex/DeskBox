using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Pure-geometry coverage for the confined-slide rules. Display-topology
/// probing (DisplayArea.FindAll) is not unit-testable; only the offset
/// resolution arithmetic is pinned here.
/// </summary>
public sealed class WidgetSlideBoundaryPolicyTests
{
    [Fact]
    public void WithoutAdjacentDisplay_OffsetIsUnchanged()
    {
        var decision = WidgetSlideBoundaryPolicy.ResolveSlideOffset(
            unconfinedOffset: 500,
            farEdge: 1800,
            workAreaEdge: 1920,
            hasAdjacentDisplay: false);

        Assert.Equal(500, decision.Offset);
        Assert.False(decision.ConfineWithFade);
    }

    [Fact]
    public void WithAdjacentDisplay_StopsFlushAtBoundary()
    {
        // Group right edge at 1700, boundary at 1920: travel 220px (above
        // the minimum), stay on this monitor, fade out on the way.
        var decision = WidgetSlideBoundaryPolicy.ResolveSlideOffset(
            unconfinedOffset: 500,
            farEdge: 1700,
            workAreaEdge: 1920,
            hasAdjacentDisplay: true);

        Assert.Equal(220, decision.Offset);
        Assert.True(decision.ConfineWithFade);
    }

    [Fact]
    public void FlushSubject_StillSlidesTheMinimumDistance()
    {
        // Subject already on the boundary: the confined distance would be 0,
        // but the slide must remain legible — keep a minimum travel that may
        // briefly cross onto the adjacent display under the accelerated fade.
        var decision = WidgetSlideBoundaryPolicy.ResolveSlideOffset(
            unconfinedOffset: 500,
            farEdge: 1920,
            workAreaEdge: 1920,
            hasAdjacentDisplay: true);

        Assert.Equal(WidgetSlideBoundaryPolicy.MinConfinedSlidePx, decision.Offset);
        Assert.True(decision.ConfineWithFade);
    }

    [Fact]
    public void NearFlushSubject_KeepsAtLeastTheMinimumTravel()
    {
        var decision = WidgetSlideBoundaryPolicy.ResolveSlideOffset(
            unconfinedOffset: 500,
            farEdge: 1900,
            workAreaEdge: 1920,
            hasAdjacentDisplay: true);

        Assert.Equal(WidgetSlideBoundaryPolicy.MinConfinedSlidePx, decision.Offset);
        Assert.True(decision.ConfineWithFade);
    }

    [Fact]
    public void MinimumTravel_NeverExceedsTheUnconfinedOffset()
    {
        // Very narrow subject: the unconfined target itself is shorter than
        // the minimum travel — the slide cannot grow beyond its own design.
        var decision = WidgetSlideBoundaryPolicy.ResolveSlideOffset(
            unconfinedOffset: 100,
            farEdge: 1920,
            workAreaEdge: 1920,
            hasAdjacentDisplay: true);

        Assert.Equal(100, decision.Offset);
        Assert.True(decision.ConfineWithFade);
    }

    [Fact]
    public void SignsArePreservedForOppositeDirections()
    {
        var left = WidgetSlideBoundaryPolicy.ResolveSlideOffset(
            unconfinedOffset: -500,
            farEdge: 20,
            workAreaEdge: 0,
            hasAdjacentDisplay: true);

        Assert.Equal(-WidgetSlideBoundaryPolicy.MinConfinedSlidePx, left.Offset);
        Assert.True(left.ConfineWithFade);

        var unconfinedLeft = WidgetSlideBoundaryPolicy.ResolveSlideOffset(
            unconfinedOffset: -500,
            farEdge: 20,
            workAreaEdge: 0,
            hasAdjacentDisplay: false);

        Assert.Equal(-500, unconfinedLeft.Offset);
    }
}
