using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace HermesLauncher.Controls;

public class ConstellationCanvas : FrameworkElement
{
    private sealed class Particle
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
        public double Radius;
        public byte BaseAlpha;
    }

    private readonly List<Particle> _particles = new();
    private readonly Random _rand = new();
    private bool _isHooked;
    private DateTime _lastRenderTime = DateTime.UtcNow;

    // Pre-allocated frozen pens and brushes for zero GC allocations per frame
    private const int AlphaLevels = 24;
    private static readonly Pen[] LinePens = new Pen[AlphaLevels];
    private static readonly Brush[] ParticleBrushes = new Brush[AlphaLevels];
    private static readonly Brush[] GlowBrushes = new Brush[AlphaLevels];

    public static readonly DependencyProperty ParticleCountProperty =
        DependencyProperty.Register(nameof(ParticleCount), typeof(int), typeof(ConstellationCanvas),
            new FrameworkPropertyMetadata(45, OnConfigurationChanged));

    public static readonly DependencyProperty MaxDistanceProperty =
        DependencyProperty.Register(nameof(MaxDistance), typeof(double), typeof(ConstellationCanvas),
            new FrameworkPropertyMetadata(115.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ParticleColorProperty =
        DependencyProperty.Register(nameof(ParticleColor), typeof(Color), typeof(ConstellationCanvas),
            new FrameworkPropertyMetadata(Color.FromRgb(0x38, 0xBD, 0xF8), OnColorChanged));

    static ConstellationCanvas()
    {
        InitializeBrushes(Color.FromRgb(0x38, 0xBD, 0xF8));
    }

    public ConstellationCanvas()
    {
        IsHitTestVisible = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public int ParticleCount
    {
        get => (int)GetValue(ParticleCountProperty);
        set => SetValue(ParticleCountProperty, value);
    }

    public double MaxDistance
    {
        get => (double)GetValue(MaxDistanceProperty);
        set => SetValue(MaxDistanceProperty, value);
    }

    public Color ParticleColor
    {
        get => (Color)GetValue(ParticleColorProperty);
        set => SetValue(ParticleColorProperty, value);
    }

    private static void OnConfigurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ConstellationCanvas canvas)
        {
            canvas.ReinitParticles();
        }
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is Color color)
        {
            InitializeBrushes(color);
        }
    }

    private static void InitializeBrushes(Color baseColor)
    {
        for (int i = 0; i < AlphaLevels; i++)
        {
            double ratio = (i + 1) / (double)AlphaLevels;
            byte lineAlpha = (byte)(ratio * 65); // Subtle translucent lines
            byte particleAlpha = (byte)(110 + ratio * 145);
            byte glowAlpha = (byte)(ratio * 40);

            var lineBrush = new SolidColorBrush(Color.FromArgb(lineAlpha, baseColor.R, baseColor.G, baseColor.B));
            lineBrush.Freeze();
            var pen = new Pen(lineBrush, 1.0);
            pen.Freeze();
            LinePens[i] = pen;

            var pBrush = new SolidColorBrush(Color.FromArgb(particleAlpha, baseColor.R, baseColor.G, baseColor.B));
            pBrush.Freeze();
            ParticleBrushes[i] = pBrush;

            var gBrush = new SolidColorBrush(Color.FromArgb(glowAlpha, baseColor.R, baseColor.G, baseColor.B));
            gBrush.Freeze();
            GlowBrushes[i] = gBrush;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ReinitParticles();
        StartAnimation();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAnimation();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            StartAnimation();
        else
            StopAnimation();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 10 && e.NewSize.Height > 10 && _particles.Count == 0)
        {
            ReinitParticles();
        }
    }

    private void StartAnimation()
    {
        if (!_isHooked)
        {
            _lastRenderTime = DateTime.UtcNow;
            CompositionTarget.Rendering += OnRendering;
            _isHooked = true;
        }
    }

    private void StopAnimation()
    {
        if (_isHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _isHooked = false;
        }
    }

    private void ReinitParticles()
    {
        _particles.Clear();
        double w = ActualWidth > 50 ? ActualWidth : 1100;
        double h = ActualHeight > 50 ? ActualHeight : 800;
        int count = ParticleCount;

        for (int i = 0; i < count; i++)
        {
            double speed = 0.25 + _rand.NextDouble() * 0.45;
            double angle = _rand.NextDouble() * Math.PI * 2;

            _particles.Add(new Particle
            {
                X = _rand.NextDouble() * w,
                Y = _rand.NextDouble() * h,
                Vx = Math.Cos(angle) * speed,
                Vy = Math.Sin(angle) * speed,
                Radius = 1.6 + _rand.NextDouble() * 1.8,
                BaseAlpha = (byte)_rand.Next(14, AlphaLevels)
            });
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsVisible || ActualWidth <= 10 || ActualHeight <= 10)
            return;

        var now = DateTime.UtcNow;
        double dt = (now - _lastRenderTime).TotalSeconds;
        _lastRenderTime = now;

        // Clamp delta time to avoid large jumps if window is moved or frozen
        if (dt > 0.08) dt = 0.016;
        double speedMult = dt * 60.0;

        double w = ActualWidth;
        double h = ActualHeight;

        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            p.X += p.Vx * speedMult;
            p.Y += p.Vy * speedMult;

            // Soft bounce off edges
            if (p.X < 0) { p.X = 0; p.Vx = Math.Abs(p.Vx); }
            else if (p.X > w) { p.X = w; p.Vx = -Math.Abs(p.Vx); }

            if (p.Y < 0) { p.Y = 0; p.Vy = Math.Abs(p.Vy); }
            else if (p.Y > h) { p.Y = h; p.Vy = -Math.Abs(p.Vy); }
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (_particles.Count == 0)
            return;

        double maxDist = MaxDistance;
        double maxDistSq = maxDist * maxDist;
        int count = _particles.Count;

        // 1. Draw connecting lines between particles close to each other
        for (int i = 0; i < count; i++)
        {
            var p1 = _particles[i];
            for (int j = i + 1; j < count; j++)
            {
                var p2 = _particles[j];
                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double distSq = dx * dx + dy * dy;

                if (distSq < maxDistSq)
                {
                    double dist = Math.Sqrt(distSq);
                    double ratio = 1.0 - (dist / maxDist);
                    int level = (int)(ratio * (AlphaLevels - 1));
                    if (level >= 0 && level < AlphaLevels)
                    {
                        var pen = LinePens[level];
                        if (pen != null)
                        {
                            dc.DrawLine(pen, new Point(p1.X, p1.Y), new Point(p2.X, p2.Y));
                        }
                    }
                }
            }
        }

        // 2. Draw particle nodes (center dot + soft outer glow)
        for (int i = 0; i < count; i++)
        {
            var p = _particles[i];
            var center = new Point(p.X, p.Y);
            int level = Math.Clamp((int)p.BaseAlpha, 0, AlphaLevels - 1);

            // Outer soft glow
            var glowBrush = GlowBrushes[level];
            if (glowBrush != null)
            {
                dc.DrawEllipse(glowBrush, null, center, p.Radius * 2.4, p.Radius * 2.4);
            }

            // Core dot
            var coreBrush = ParticleBrushes[level];
            if (coreBrush != null)
            {
                dc.DrawEllipse(coreBrush, null, center, p.Radius, p.Radius);
            }
        }
    }
}
