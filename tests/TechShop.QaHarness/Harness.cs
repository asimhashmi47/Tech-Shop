using System.Drawing;
using System.Drawing.Imaging;

namespace TechShop.QaHarness;

/// <summary>
/// Collects results and prints a report. Nothing here stops on the first failure: the
/// point of the run is to find every broken screen in one pass, not the first one.
/// </summary>
public sealed class Harness
{
    private readonly List<Result> _results = [];
    private string _section = "general";

    private sealed record Result(string Section, string Name, bool Passed, string? Detail);

    public int Failures => _results.Count(r => !r.Passed);

    public void Section(string name)
    {
        _section = name;
        Console.WriteLine();
        Console.WriteLine($"--- {name}");
    }

    public void Pass(string name, string? detail = null)
    {
        _results.Add(new Result(_section, name, true, detail));
        Console.WriteLine($"  ok    {name}{(detail is null ? "" : $"  ({detail})")}");
    }

    public void Fail(string name, string detail)
    {
        _results.Add(new Result(_section, name, false, detail));
        Console.WriteLine($"  FAIL  {name}");
        Console.WriteLine($"          {Flatten(detail)}");
    }

    /// <summary>Runs a check, turning any exception into a failure rather than a crash.</summary>
    public void Check(string name, Action action, string? detail = null)
    {
        try
        {
            action();
            Pass(name, detail);
        }
        catch (Exception ex)
        {
            Fail(name, Describe(ex));
        }
    }

    public async Task CheckAsync(string name, Func<Task> action, string? detail = null)
    {
        try
        {
            await action().ConfigureAwait(true);
            Pass(name, detail);
        }
        catch (Exception ex)
        {
            Fail(name, Describe(ex));
        }
    }

    public void Expect(string name, bool condition, string whenFalse)
    {
        if (condition)
        {
            Pass(name);
        }
        else
        {
            Fail(name, whenFalse);
        }
    }

    /// <summary>
    /// Builds a control and forces a full paint into a bitmap.
    ///
    /// Constructing a WinForms control proves very little — most of this app's drawing
    /// happens in Paint handlers, which never run until something asks for pixels. This
    /// is what catches a null dereference in OnPaint, and it is how the first round of
    /// layout crashes were found.
    /// </summary>
    public void Render(string name, Func<Control> build, int width = 1280, int height = 820)
    {
        Control? control = null;

        try
        {
            control = build();
            control.Size = new Size(width, height);

            // Forces a layout pass and every Paint handler in the tree.
            using var bitmap = new Bitmap(Math.Max(control.Width, 1), Math.Max(control.Height, 1), PixelFormat.Format32bppArgb);
            control.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

            Expect($"{name} renders", !IsBlank(bitmap), "painted nothing at all");
        }
        catch (Exception ex)
        {
            Fail($"{name} renders", Describe(ex));
        }
        finally
        {
            control?.Dispose();
        }
    }

    /// <summary>
    /// A screen that drew nothing is almost always a layout bug rather than an empty
    /// state, so a fully uniform bitmap is reported as a failure.
    /// </summary>
    private static bool IsBlank(Bitmap bitmap)
    {
        var first = bitmap.GetPixel(0, 0);

        for (var x = 0; x < bitmap.Width; x += 11)
        {
            for (var y = 0; y < bitmap.Height; y += 11)
            {
                if (bitmap.GetPixel(x, y) != first)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool Report()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 70));

        var bySection = _results.GroupBy(r => r.Section);

        foreach (var section in bySection)
        {
            var failed = section.Count(r => !r.Passed);
            var total = section.Count();
            Console.WriteLine($"  {section.Key,-28} {total - failed,3}/{total,-3} passed{(failed > 0 ? $"   {failed} FAILED" : "")}");
        }

        Console.WriteLine(new string('=', 70));

        if (Failures == 0)
        {
            Console.WriteLine($"ALL {_results.Count} CHECKS PASSED");
            return true;
        }

        Console.WriteLine($"{Failures} of {_results.Count} CHECKS FAILED");
        Console.WriteLine();

        foreach (var failure in _results.Where(r => !r.Passed))
        {
            Console.WriteLine($"  [{failure.Section}] {failure.Name}");
            Console.WriteLine($"      {Flatten(failure.Detail ?? string.Empty)}");
        }

        return false;
    }

    private static string Describe(Exception ex)
    {
        // The innermost exception is the one that says what actually broke; the outer
        // TargetInvocation / AggregateException wrappers only say where it surfaced.
        var root = ex;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
        }

        var frame = root.StackTrace?
            .Split('\n')
            .FirstOrDefault(l => l.Contains("TechShop", StringComparison.Ordinal))
            ?.Trim();

        return $"{root.GetType().Name}: {root.Message}{(frame is null ? "" : $"  at {frame}")}";
    }

    private static string Flatten(string value)
        => value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
