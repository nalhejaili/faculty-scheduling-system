using System;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System;

namespace MiniTrainerScheduler.Services
{
    public static class PrintService
    {
        public static void PrintElement(FrameworkElement elementToPrint, string jobName = "Academic Timetable")
        {
            if (elementToPrint == null) return;

            elementToPrint.FlowDirection = FlowDirection.LeftToRight;
            elementToPrint.Language = XmlLanguage.GetLanguage("en-US");
            TextOptions.SetTextFormattingMode(elementToPrint, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(elementToPrint, TextRenderingMode.Auto);

            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;

            Size origSize = new Size(
                elementToPrint.ActualWidth > 0 ? elementToPrint.ActualWidth : elementToPrint.Width,
                elementToPrint.ActualHeight > 0 ? elementToPrint.ActualHeight : elementToPrint.Height);

            if (double.IsNaN(origSize.Width) || origSize.Width <= 0 ||
                double.IsNaN(origSize.Height) || origSize.Height <= 0)
            {
                elementToPrint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                elementToPrint.Arrange(new Rect(elementToPrint.DesiredSize));
                origSize = elementToPrint.DesiredSize;
            }

            var pageSize = new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);

            const double dpi = 300.0;
            double scale = dpi / 96.0;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var vb = new VisualBrush(elementToPrint)
                {
                    Stretch = Stretch.Uniform,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };

                dc.DrawRectangle(vb, null, new Rect(new Point(0, 0), pageSize));
            }

            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(pageSize.Width * scale),
                (int)Math.Ceiling(pageSize.Height * scale),
                dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);

            var fixedDoc = new FixedDocument { };
            fixedDoc.DocumentPaginator.PageSize = pageSize;

            var page = new FixedPage { Width = pageSize.Width, Height = pageSize.Height };
            var img = new Image { Source = rtb, Width = page.Width, Height = page.Height, Stretch = Stretch.Fill };
            FixedPage.SetLeft(img, 0);
            FixedPage.SetTop(img, 0);
            page.Children.Add(img);

            var pc = new PageContent();
            ((IAddChild)pc).AddChild(page);
            fixedDoc.Pages.Add(pc);

            dlg.PrintDocument(fixedDoc.DocumentPaginator, jobName);
        }
        private static void FixMirrorTransforms(DependencyObject root)
        {
            if (root is FrameworkElement fe)
            {
                if (fe.LayoutTransform is ScaleTransform lt && lt.ScaleX < 0)
                    fe.LayoutTransform = new ScaleTransform(Math.Abs(lt.ScaleX), lt.ScaleY);

                if (fe.RenderTransform is ScaleTransform rt && rt.ScaleX < 0)
                    fe.RenderTransform = new ScaleTransform(Math.Abs(rt.ScaleX), rt.ScaleY);
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                FixMirrorTransforms(child);
            }
        }

        private sealed class TransformSnapshot
        {
            public FrameworkElement Element { get; }
            public Transform? Layout { get; }
            public Transform? Render { get; }

            public TransformSnapshot(FrameworkElement element, Transform? layout, Transform? render)
            {
                Element = element;
                Layout = layout;
                Render = render;
            }
        }

        private static void FixMirrorTransforms(DependencyObject root, out List<TransformSnapshot> snapshots)
        {
            snapshots = new List<TransformSnapshot>();
            TraverseAndFix(root, snapshots);
        }

        private static void TraverseAndFix(DependencyObject obj, List<TransformSnapshot> snapshots)
        {
            if (obj is FrameworkElement fe)
            {
                bool changed = false;
                Transform? origLayout = fe.LayoutTransform;
                Transform? origRender = fe.RenderTransform;

                if (fe.LayoutTransform is ScaleTransform lt && lt.ScaleX < 0)
                {
                    fe.LayoutTransform = new ScaleTransform(Math.Abs(lt.ScaleX), lt.ScaleY);
                    changed = true;
                }

                if (fe.RenderTransform is ScaleTransform rt && rt.ScaleX < 0)
                {
                    fe.RenderTransform = new ScaleTransform(Math.Abs(rt.ScaleX), rt.ScaleY);
                    changed = true;
                }

                if (changed)
                {
                    snapshots.Add(new TransformSnapshot(fe, origLayout, origRender));
                }
            }

            int count = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                TraverseAndFix(child, snapshots);
            }
        }

        private static void RestoreTransforms(List<TransformSnapshot> snapshots)
        {
            foreach (var snap in snapshots)
            {
                snap.Element.LayoutTransform = snap.Layout;
                snap.Element.RenderTransform = snap.Render;
            }
        }

        public static void PrintDepartmentElement(FrameworkElement elementToPrint, string jobName = "Department Timetable")
        {
            if (elementToPrint == null) return;

            string xaml;
            try
            {
                xaml = XamlWriter.Save(elementToPrint);
            }
            catch
            {
                PrintElement(elementToPrint, jobName);
                return;
            }

            var clone = (FrameworkElement)XamlReader.Parse(xaml);

            clone.DataContext = elementToPrint.DataContext;
            clone.FlowDirection = FlowDirection.LeftToRight;

            FixMirrorTransforms(clone);

            var dlg = new PrintDialog();
            if (dlg.PrintTicket != null)
                dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;

            if (dlg.ShowDialog() != true)
                return;

            Size pageSize = new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);

            const double dpi = 300.0;
            double scale = dpi / 96.0;

            clone.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            clone.Arrange(new Rect(clone.DesiredSize));
            clone.UpdateLayout();

            double w = clone.ActualWidth > 0 ? clone.ActualWidth : clone.DesiredSize.Width;
            double h = clone.ActualHeight > 0 ? clone.ActualHeight : clone.DesiredSize.Height;
            if (w <= 0 || h <= 0) return;

            var rtb = new RenderTargetBitmap(
                (int)Math.Round(w * scale),
                (int)Math.Round(h * scale),
                dpi, dpi, PixelFormats.Pbgra32);

            rtb.Render(clone);

            var img = new Image { Source = rtb, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var vb = new Viewbox
            {
                Width = pageSize.Width,
                Height = pageSize.Height,
                Stretch = Stretch.Uniform,
                Child = img
            };

            var page = new FixedPage
            {
                Width = pageSize.Width,
                Height = pageSize.Height,
                Background = Brushes.White
            };
            FixedPage.SetLeft(vb, 0);
            FixedPage.SetTop(vb, 0);
            page.Children.Add(vb);

            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = pageSize;
            var pc = new PageContent();
            ((IAddChild)pc).AddChild(page);
            doc.Pages.Add(pc);

            dlg.PrintDocument(doc.DocumentPaginator, jobName);
        }

        public static void PrintElementUnmirrored(FrameworkElement elementToPrint, string jobName = "University Master Timetable")
        {
            if (elementToPrint == null) return;

            var dlg = new PrintDialog();
            if (dlg.PrintTicket != null)
                dlg.PrintTicket.PageOrientation = System.Printing.PageOrientation.Landscape;

            if (dlg.ShowDialog() != true) return;

            Size pageSize = new(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var vb = new VisualBrush(elementToPrint)
                {
                    Stretch = Stretch.Uniform,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };

                // English presentation mode: render exactly as shown on screen without horizontal mirroring.
                dc.DrawRectangle(vb, null, new Rect(new Point(0, 0), pageSize));
            }

            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = pageSize;

            var page = new FixedPage { Width = pageSize.Width, Height = pageSize.Height };
            var presenter = new _DrawingPresenter(dv) { Width = page.Width, Height = page.Height };
            FixedPage.SetLeft(presenter, 0);
            FixedPage.SetTop(presenter, 0);
            page.Children.Add(presenter);

            var pc = new PageContent();
            ((IAddChild)pc).AddChild(page);
            doc.Pages.Add(pc);

            dlg.PrintDocument(doc.DocumentPaginator, jobName);
        }

        private sealed class _DrawingPresenter : FrameworkElement
        {
            private readonly Visual _visual;
            public _DrawingPresenter(Visual visual) => _visual = visual;
            protected override int VisualChildrenCount => 1;
            protected override Visual GetVisualChild(int index) => _visual;
        }
    }
}
