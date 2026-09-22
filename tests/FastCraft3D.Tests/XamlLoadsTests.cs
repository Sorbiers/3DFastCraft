using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastCraft3D.Geometry;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The markup loads at all.
///
/// A compiler will not catch two resources under one key: the markup builds, the app starts, the
/// dictionary throws as it is put together, and the window never appears - with nothing on the
/// console to say why, since there is no console. That is exactly how the icons were broken, and
/// it was found by running the app rather than by anything here, which is what this is for.
/// </summary>
[Collection("Tool panels")]
public class XamlLoadsTests
{
    private static void OnStaThread(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void TheIconsLoadWithNoKeyGivenTwice()
    {
        OnStaThread(() =>
        {
            var uri = new Uri("/3DFastCraft;component/View/Icons.xaml", UriKind.Relative);

            // Nothing to assert beyond it coming back: a key given twice throws here, which is
            // the failure this is watching for.
            var icons = (ResourceDictionary)Application.LoadComponent(uri);

            Assert.NotEmpty(icons.Keys);
            Assert.Contains("IconLithophane", icons.Keys.Cast<object>().Select(k => k.ToString()));
        });
    }

    /// <summary>
    /// The lithophane panel is a grid of numbers each with a slider beside it, wired up by name
    /// in the constructor. A name that does not match anything in the markup is a compile error,
    /// but a slider left unpaired is not, and neither is markup that only fails as it is read.
    /// </summary>
    [Fact]
    public void TheLithophanePanelOpensOnAPicture()
    {
        OnStaThread(() =>
        {
            string file = Path.Combine(Path.GetTempPath(), $"fc3d-litho-{Guid.NewGuid():N}.png");
            WriteGreyPng(file, 8, 6);

            try
            {
                Mesh? shown = null;
                var panel = new LithophaneDialog(file, new LithophaneOptions(), mesh => shown = mesh);

                // The plate is on the build plate as the panel opens, not a moment afterwards.
                Assert.NotNull(shown);
                Assert.True(shown!.TriangleCount > 0);
                Assert.Null(panel.Result);

                // Marked in the markup as still being proved out, which the panel host draws a
                // badge from - and a mark that quietly stopped parsing would be found here.
                Assert.True(panel.IsBeta);

                panel.Close();
            }
            finally
            {
                File.Delete(file);
            }
        });
    }

    /// <summary>A picture to feed it: a grey ramp, which is what a lithophane is for.</summary>
    private static void WriteGreyPng(string file, int width, int height)
    {
        var pixels = new byte[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 255 / Math.Max(1, pixels.Length - 1));

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
