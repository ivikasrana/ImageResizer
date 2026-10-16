using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ImageResizer;

/// <summary>Night-sky backdrop for the dark theme: twinkling stars, soft glows and a few slowly rising golden sparks.</summary>
public sealed class StarField : Canvas
{
    record Star(Ellipse Shape, double X, double Y);

    readonly List<Star> _stars = [];
    readonly List<Ellipse> _glows = [];
    readonly List<(IAnimatable Target, DependencyProperty Property, AnimationTimeline Animation)> _anims = [];
    readonly Random _rnd = new();
    bool _built;

    public StarField()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Loaded += (_, _) => Build();
        SizeChanged += (_, _) => Layout();
        IsVisibleChanged += (_, _) => Run(IsVisible);
    }

    double R(double a, double b) => a + _rnd.NextDouble() * (b - a);

    static Duration Secs(double s) => new(TimeSpan.FromSeconds(s));

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    static readonly Brush StarBrush = Frozen(new SolidColorBrush(Color.FromRgb(243, 234, 217)));
    static readonly Brush GoldStarBrush = Frozen(new SolidColorBrush(Color.FromRgb(247, 197, 107)));
    static readonly Brush SparkBrush = Frozen(new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Color.FromArgb(255, 247, 197, 107), 0),
            new GradientStop(Color.FromArgb(210, 247, 197, 107), 0.3),
            new GradientStop(Color.FromArgb(0, 247, 197, 107), 1),
        },
    });

    void Animate(IAnimatable target, DependencyProperty property, AnimationTimeline animation)
    {
        Timeline.SetDesiredFrameRate(animation, 30);
        _anims.Add((target, property, animation));
    }

    void Run(bool on)
    {
        foreach (var (target, property, animation) in _anims) target.BeginAnimation(property, on ? animation : null);
    }

    void Build()
    {
        if (_built) return;
        _built = true;

        // soft blue / amber glows
        foreach (var (color, alpha, left) in new[] { (Color.FromRgb(63, 111, 163), (byte)70, true), (Color.FromRgb(217, 112, 44), (byte)45, false) })
        {
            var glow = new Ellipse
            {
                Fill = Frozen(new RadialGradientBrush(Color.FromArgb(alpha, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B))),
                Tag = left,
                CacheMode = new BitmapCache(),
            };
            Animate(glow, OpacityProperty, new DoubleAnimation(0.55, 1, Secs(R(14, 22))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            Children.Add(glow);
            _glows.Add(glow);
        }

        // twinkling stars
        for (int i = 0; i < 70; i++)
        {
            var size = R(1.2, 2.4);
            var star = new Ellipse
            {
                Width = size, Height = size, Opacity = 0.4,
                Fill = i % 9 == 0 ? GoldStarBrush : StarBrush,
            };
            Animate(star, OpacityProperty, new DoubleAnimation(0.25, 0.95, Secs(R(4, 9)))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(R(0, 6)),
            });
            Children.Add(star);
            _stars.Add(new Star(star, _rnd.NextDouble(), _rnd.NextDouble()));
        }

        // a few golden sparks drifting upward
        var rise = SystemParameters.PrimaryScreenHeight + 60;
        for (int i = 0; i < 14; i++)
        {
            var size = R(4, 7);
            var p = new Ellipse
            {
                Width = size, Height = size, Opacity = 0,
                Fill = SparkBrush,
                RenderTransform = new TranslateTransform(),
            };
            var drift = R(-30, 30);
            var dur = Secs(R(18, 34));
            var begin = TimeSpan.FromSeconds(R(0, 20));
            var t = (TranslateTransform)p.RenderTransform;
            Animate(t, TranslateTransform.YProperty, new DoubleAnimation(0, -rise, dur) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            Animate(t, TranslateTransform.XProperty, new DoubleAnimation(0, drift, dur) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            Animate(p, OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                Duration = dur, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin,
                KeyFrames =
                {
                    new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)),
                    new LinearDoubleKeyFrame(0.75, KeyTime.FromPercent(0.15)),
                    new LinearDoubleKeyFrame(0.35, KeyTime.FromPercent(0.8)),
                    new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)),
                },
            });
            SetLeft(p, R(0, 1) * 1000); // refined in Layout()
            SetBottom(p, -12);
            p.Tag = _rnd.NextDouble();
            Children.Add(p);
        }
        Layout();
        Run(IsVisible);
    }

    void Layout()
    {
        if (!_built || ActualWidth <= 0) return;
        foreach (var s in _stars) { SetLeft(s.Shape, s.X * ActualWidth); SetTop(s.Shape, s.Y * ActualHeight); }
        foreach (var g in _glows)
        {
            var d = Math.Max(ActualWidth, ActualHeight) * 0.7;
            g.Width = g.Height = d;
            if ((bool)g.Tag!) { SetLeft(g, -d * 0.3); SetTop(g, ActualHeight * 0.05); }
            else { SetLeft(g, ActualWidth - d * 0.7); SetTop(g, ActualHeight - d * 0.7); }
        }
        foreach (var e in Children.OfType<Ellipse>().Where(e => e.RenderTransform is TranslateTransform))
            SetLeft(e, (double)e.Tag! * ActualWidth);
    }
}
