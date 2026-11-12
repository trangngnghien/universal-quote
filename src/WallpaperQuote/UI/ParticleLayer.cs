using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperQuote.Core;

namespace WallpaperQuote.UI
{
    /// <summary>
    /// Lớp hạt rơi (W7): tuyết, mưa, hoa. Vẽ bằng DrawingContext ~30 khung hình/giây.
    /// Có 2 chế độ: rơi liên tục theo mật độ đã chọn, và "thả một đợt" dày hơn trong vài giây.
    /// </summary>
    public sealed class ParticleLayer : FrameworkElement
    {
        private struct Particle
        {
            public double X, Y, VX, VY, Size, Angle, Spin, Sway, Phase;
            public int Shade;
            public string Kind;
        }

        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Random _random = new Random();
        private readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        private DateTime _last = DateTime.UtcNow;

        private string _type = EffectType.None;
        private int _baseCount;
        private string _burstType = EffectType.Snow;
        private DateTime _burstUntil = DateTime.MinValue;
        private double _spawnDebt;

        private static readonly Brush[] SnowBrushes = MakeBrushes(Color.FromRgb(255, 255, 255), 0.55, 0.95);
        private static readonly Brush[] PetalBrushes =
        {
            Freeze(new SolidColorBrush(Color.FromArgb(230, 249, 168, 212))),
            Freeze(new SolidColorBrush(Color.FromArgb(230, 244, 114, 182))),
            Freeze(new SolidColorBrush(Color.FromArgb(220, 253, 205, 225))),
            Freeze(new SolidColorBrush(Color.FromArgb(220, 255, 228, 236)))
        };
        private static readonly Pen[] RainPens =
        {
            FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(110, 200, 220, 255)), 1.2)),
            FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(150, 210, 228, 255)), 1.5)),
            FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(90, 190, 210, 245)), 1.0))
        };

        /// <summary>Không còn hạt nào và không còn đợt thả → có thể ẩn lớp hiệu ứng.</summary>
        public event Action? Idle;

        public ParticleLayer()
        {
            IsHitTestVisible = false;
            _timer.Tick += (_, __) => Step();
        }

        public bool IsActive => _timer.IsEnabled;

        /// <summary>Đặt hiệu ứng rơi liên tục. <paramref name="type"/> = none để tắt (hạt đang rơi rơi nốt).</summary>
        public void Configure(string type, string intensity)
        {
            _type = type;
            _baseCount = type == EffectType.None ? 0 : BaseCount(type, intensity);
            EnsureRunning();
        }

        /// <summary>Thả một đợt dày trong <paramref name="duration"/>.</summary>
        public void Burst(string type, TimeSpan duration)
        {
            _burstType = type == EffectType.None ? EffectType.Snow : type;
            _burstUntil = DateTime.UtcNow + duration;
            EnsureRunning();
        }

        public void Stop()
        {
            _timer.Stop();
            _particles.Clear();
            InvalidateVisual();
        }

        private static int BaseCount(string type, string intensity)
        {
            var factor = intensity == EffectIntensity.Light ? 0.5 : intensity == EffectIntensity.Heavy ? 1.8 : 1.0;
            var n = type == EffectType.Rain ? 160 : type == EffectType.Petals ? 45 : 110;
            return (int)(n * factor);
        }

        private void EnsureRunning()
        {
            if (_timer.IsEnabled) return;
            _last = DateTime.UtcNow;
            _timer.Start();
        }

        private void Step()
        {
            var now = DateTime.UtcNow;
            var dt = Math.Min(0.1, (now - _last).TotalSeconds);
            _last = now;
            var w = ActualWidth;
            var h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            var bursting = now < _burstUntil;
            var target = _baseCount + (bursting ? BaseCount(_burstType, EffectIntensity.Heavy) * 2 : 0);

            // Sinh hạt dần dần từ mép trên để không xuất hiện đột ngột.
            if (_particles.Count < target)
            {
                _spawnDebt += target * dt / 2.5;
                while (_spawnDebt >= 1 && _particles.Count < target)
                {
                    _spawnDebt -= 1;
                    var kind = bursting && (_baseCount == 0 || _random.NextDouble() < 0.6) ? _burstType : _type;
                    if (kind == EffectType.None) kind = _burstType;
                    _particles.Add(Spawn(kind, w, h, fromTop: true));
                }
            }
            else
            {
                _spawnDebt = 0;
            }

            for (var i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Phase += dt;
                p.X += (p.VX + Math.Sin(p.Phase * 1.3 + p.Sway) * p.Sway * 12) * dt;
                p.Y += p.VY * dt;
                p.Angle += p.Spin * dt;
                if (p.Y > h + 30 || p.X < -40 || p.X > w + 40)
                {
                    // Hạt rơi khỏi màn hình: tái sinh nếu còn cần, không thì bỏ.
                    if (_particles.Count <= target) _particles[i] = Spawn(p.Kind, w, h, fromTop: true);
                    else _particles.RemoveAt(i);
                    continue;
                }
                _particles[i] = p;
            }

            InvalidateVisual();

            if (_particles.Count == 0 && target == 0)
            {
                _timer.Stop();
                Idle?.Invoke();
            }
        }

        private Particle Spawn(string kind, double w, double h, bool fromTop)
        {
            var p = new Particle
            {
                Kind = kind,
                X = _random.NextDouble() * w,
                Y = fromTop ? -20 - _random.NextDouble() * 40 : _random.NextDouble() * h,
                Phase = _random.NextDouble() * 10,
                Shade = _random.Next(4)
            };
            switch (kind)
            {
                case EffectType.Rain:
                    p.Size = 14 + _random.NextDouble() * 18;
                    p.VY = 650 + _random.NextDouble() * 350;
                    p.VX = -60;
                    p.Sway = 0;
                    break;
                case EffectType.Petals:
                    p.Size = 6 + _random.NextDouble() * 7;
                    p.VY = 45 + _random.NextDouble() * 55;
                    p.VX = 15 + _random.NextDouble() * 25;
                    p.Sway = 2 + _random.NextDouble() * 3;
                    p.Spin = (_random.NextDouble() - 0.5) * 160;
                    p.Angle = _random.NextDouble() * 360;
                    break;
                default:
                    p.Size = 1.5 + _random.NextDouble() * 3.5;
                    p.VY = 35 + _random.NextDouble() * 70;
                    p.VX = (_random.NextDouble() - 0.5) * 20;
                    p.Sway = 0.5 + _random.NextDouble() * 1.5;
                    break;
            }
            return p;
        }

        protected override void OnRender(DrawingContext dc)
        {
            foreach (var p in _particles)
            {
                switch (p.Kind)
                {
                    case EffectType.Rain:
                        dc.DrawLine(RainPens[p.Shade % RainPens.Length], new Point(p.X, p.Y), new Point(p.X + p.VX * 0.02, p.Y + p.Size));
                        break;
                    case EffectType.Petals:
                        dc.PushTransform(new RotateTransform(p.Angle, p.X, p.Y));
                        dc.DrawEllipse(PetalBrushes[p.Shade % PetalBrushes.Length], null, new Point(p.X, p.Y), p.Size, p.Size * 0.55);
                        dc.Pop();
                        break;
                    default:
                        dc.DrawEllipse(SnowBrushes[p.Shade % SnowBrushes.Length], null, new Point(p.X, p.Y), p.Size, p.Size);
                        break;
                }
            }
        }

        private static Brush[] MakeBrushes(Color c, double minAlpha, double maxAlpha)
        {
            var list = new Brush[4];
            for (var i = 0; i < 4; i++)
            {
                var a = (byte)(255 * (minAlpha + (maxAlpha - minAlpha) * i / 3.0));
                list[i] = Freeze(new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)));
            }
            return list;
        }

        private static Brush Freeze(SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }

        private static Pen FreezePen(Pen p)
        {
            p.Freeze();
            return p;
        }
    }
}
