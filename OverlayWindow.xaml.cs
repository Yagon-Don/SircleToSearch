using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Bitmap = System.Drawing.Bitmap;
using Rectangle = System.Drawing.Rectangle;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SircleToSearch;

public partial class OverlayWindow : Window
{
    private const double HandleSize = 14;
    private const double MinSelectionSize = 15;

    private enum DragMode { None, Creating, Moving, Resizing }

    private Bitmap? _screenshot;
    private ResultWindow? _resultWindow;

    private DragMode _mode = DragMode.None;
    private Point _dragAnchor;
    private Rect _selection = Rect.Empty;

    public OverlayWindow()
    {
        InitializeComponent();

        Loaded += OverlayWindow_Loaded;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter && !_selection.IsEmpty) StartSearch(_selection);
        };
    }

    private void OverlayWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        var (bitmap, _) = ScreenCapture.CaptureVirtualScreen();
        _screenshot = bitmap;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        ScreenshotImage.Source = ToBitmapSource(bitmap);
        UpdateDimOverlay(Rect.Empty);
        HintText.Text = Strings.Get("OverlayHint");

        Activate();
        Focus();

        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));

        var pulse = new DoubleAnimation(0.9, 1, TimeSpan.FromSeconds(1.4))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        HintText.BeginAnimation(OpacityProperty, pulse);
    }

    private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(RootGrid);

        var oppositeCorner = HitCorner(pos);
        if (oppositeCorner.HasValue)
        {
            _mode = DragMode.Resizing;
            _dragAnchor = oppositeCorner.Value;
        }
        else if (!_selection.IsEmpty && _selection.Contains(pos))
        {
            _mode = DragMode.Moving;
            _dragAnchor = new Point(pos.X - _selection.X, pos.Y - _selection.Y);
        }
        else
        {
            _mode = DragMode.Creating;
            _dragAnchor = pos;
            _selection = new Rect(pos, pos);
        }

        RootGrid.CaptureMouse();
        UpdateSelectionVisuals();
    }

    private void RootGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_mode == DragMode.None) return;

        var pos = e.GetPosition(RootGrid);
        pos.X = Math.Clamp(pos.X, 0, ActualWidth);
        pos.Y = Math.Clamp(pos.Y, 0, ActualHeight);

        switch (_mode)
        {
            case DragMode.Creating:
            case DragMode.Resizing:
                var candidate = new Rect(
                    Math.Min(_dragAnchor.X, pos.X), Math.Min(_dragAnchor.Y, pos.Y),
                    Math.Abs(pos.X - _dragAnchor.X), Math.Abs(pos.Y - _dragAnchor.Y));
                // While actively creating, let it track the cursor even below the
                // minimum size (checked on release); while resizing an existing
                // selection, don't let it collapse to nothing mid-drag.
                if (_mode == DragMode.Creating || (candidate.Width >= MinSelectionSize && candidate.Height >= MinSelectionSize))
                    _selection = candidate;
                break;

            case DragMode.Moving:
                var width = _selection.Width;
                var height = _selection.Height;
                var newX = Math.Clamp(pos.X - _dragAnchor.X, 0, Math.Max(0, ActualWidth - width));
                var newY = Math.Clamp(pos.Y - _dragAnchor.Y, 0, Math.Max(0, ActualHeight - height));
                _selection = new Rect(newX, newY, width, height);
                break;
        }

        UpdateSelectionVisuals();
    }

    private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == DragMode.None) return;
        var mode = _mode;
        _mode = DragMode.None;
        RootGrid.ReleaseMouseCapture();

        if (_selection.Width < MinSelectionSize || _selection.Height < MinSelectionSize)
        {
            if (mode == DragMode.Creating)
            {
                // Accidental click/tiny drag with no prior selection — reset and
                // keep waiting for a real gesture.
                _selection = Rect.Empty;
                UpdateSelectionVisuals();
            }
            return;
        }

        StartSearch(_selection);
    }

    /// <summary>Returns the opposite corner (resize anchor) if <paramref name="pos"/> is near a selection handle.</summary>
    private Point? HitCorner(Point pos)
    {
        if (_selection.IsEmpty) return null;

        var corners = new (Point Corner, Point Opposite)[]
        {
            (new Point(_selection.Left, _selection.Top), new Point(_selection.Right, _selection.Bottom)),
            (new Point(_selection.Right, _selection.Top), new Point(_selection.Left, _selection.Bottom)),
            (new Point(_selection.Left, _selection.Bottom), new Point(_selection.Right, _selection.Top)),
            (new Point(_selection.Right, _selection.Bottom), new Point(_selection.Left, _selection.Top)),
        };

        foreach (var (corner, opposite) in corners)
        {
            if ((corner - pos).Length <= HandleSize)
                return opposite;
        }
        return null;
    }

    private void UpdateSelectionVisuals()
    {
        UpdateDimOverlay(_selection);

        if (_selection.IsEmpty || _selection.Width <= 0 || _selection.Height <= 0)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            Handles.Children.Clear();
            return;
        }

        SelectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBorder, _selection.X);
        Canvas.SetTop(SelectionBorder, _selection.Y);
        SelectionBorder.Width = _selection.Width;
        SelectionBorder.Height = _selection.Height;

        DrawCornerHandles(_selection);
    }

    private void DrawCornerHandles(Rect rect)
    {
        Handles.Children.Clear();
        var corners = new[]
        {
            new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top),
            new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom),
        };

        foreach (var corner in corners)
        {
            var handle = new System.Windows.Shapes.Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = System.Windows.Media.Brushes.White,
                RadiusX = 3,
                RadiusY = 3,
            };
            Canvas.SetLeft(handle, corner.X - HandleSize / 2);
            Canvas.SetTop(handle, corner.Y - HandleSize / 2);
            Handles.Children.Add(handle);
        }
    }

    private void UpdateDimOverlay(Rect selection)
    {
        var full = new RectangleGeometry(new Rect(0, 0, Math.Max(ActualWidth, 1), Math.Max(ActualHeight, 1)));
        if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0)
        {
            DimOverlay.Data = full;
            return;
        }

        var hole = new RectangleGeometry(selection);
        DimOverlay.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
    }

    private void StartSearch(Rect selection)
    {
        try
        {
            var cropped = CropToBounds(selection);
            var jpegBytes = ToJpegBytes(cropped);
            cropped.Dispose();

            // Keep the overlay open — the selection stays on screen so the user can
            // drag/resize it and re-search, instead of the whole thing vanishing
            // after one shot. Reuse the same result window across re-searches.
            if (_resultWindow is null)
            {
                _resultWindow = new ResultWindow(jpegBytes);
                _resultWindow.Closed += (_, _) => _resultWindow = null;
                _resultWindow.Show();
            }
            else
            {
                _resultWindow.UpdateSearch(jpegBytes);
            }
            _resultWindow.Activate();
        }
        catch (Exception ex)
        {
            AppLog.Error("Поиск не удался", ex);
        }
    }

    private Bitmap CropToBounds(Rect dipBounds)
    {
        var screenshot = _screenshot!;

        // No padding: with a precise drag-resizable rectangle (unlike the old freehand
        // lasso) the user's box IS the intended crop — padding it out was sending
        // noticeably more of the screen than what was actually selected.
        var scaleX = screenshot.Width / ActualWidth;
        var scaleY = screenshot.Height / ActualHeight;

        var pixelRect = new Rectangle(
            (int)(dipBounds.X * scaleX),
            (int)(dipBounds.Y * scaleY),
            (int)(dipBounds.Width * scaleX),
            (int)(dipBounds.Height * scaleY));

        pixelRect.Intersect(new Rectangle(0, 0, screenshot.Width, screenshot.Height));
        if (pixelRect.Width <= 0 || pixelRect.Height <= 0)
        {
            pixelRect = new Rectangle(0, 0, screenshot.Width, screenshot.Height);
        }

        // Bitmap.Clone(rect) is known to hand back corrupt/garbage pixel data for
        // some rectangles — drawing into a fresh bitmap is the reliable way to crop.
        var result = new Bitmap(pixelRect.Width, pixelRect.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(result))
        {
            g.DrawImage(screenshot, new Rectangle(0, 0, pixelRect.Width, pixelRect.Height),
                pixelRect, GraphicsUnit.Pixel);
        }
        return result;
    }

    private static byte[] ToJpegBytes(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _screenshot?.Dispose();
        _resultWindow?.Close();
        base.OnClosed(e);
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);
}
