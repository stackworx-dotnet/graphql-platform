using Bunit;

namespace StrawberryShake.Razor;

/// <summary>
/// Smoke tests confirming bUnit can render the StrawberryShake.Razor components.
/// </summary>
public class UseFragmentBunitTests : BunitContext
{
    [Fact]
    public void Renders_ChildContent_When_Data_Is_Present()
    {
        // arrange & act
        var cut = Render<UseFragment<string>>(parameters => parameters
            .Add(p => p.Data, "Strawberry")
            .Add(p => p.ChildContent, data => $"<span>{data}</span>"));

        // assert
        cut.MarkupMatches("<span>Strawberry</span>");
    }

    [Fact]
    public void Renders_LoadingContent_When_Data_Is_Null()
    {
        // arrange & act
        var cut = Render<UseFragment<string>>(parameters => parameters
            .Add(p => p.LoadingContent, "<em>loading</em>"));

        // assert
        cut.MarkupMatches("<em>loading</em>");
    }
}
