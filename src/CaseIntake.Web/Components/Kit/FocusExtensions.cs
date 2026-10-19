using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CaseIntake.Web.Components.Kit;

public static class FocusExtensions
{
    /// <summary>
    /// Focuses the element when it is (still) on the page. A reference that was never captured, an element removed by
    /// the last render, or a circuit that just disconnected is not an error for focus management.
    /// </summary>
    public static async ValueTask FocusSafeAsync(this ElementReference element)
    {
        if (element.Id is null)
        {
            return;
        }
        try
        {
            await element.FocusAsync();
        }
        catch (JSException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
