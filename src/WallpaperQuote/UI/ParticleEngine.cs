using System;
using System.Collections.Generic;
using WallpaperQuote.Core;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace WallpaperQuote.UI
{
    /// <summary>
    /// Hạt rơi (W7): tuyết, mưa, hoa. Tự chạy ~30 khung hình/giây và báo <see cref="Changed"/> để cửa sổ vẽ lại.
    /// Có 2 chế độ: rơi liên tục theo mật độ đã chọn, và "thả một đợt" dày hơn trong N giây.
    /// </summary>
    public sealed class ParticleEngine : IDisposable
    {
        private struct Particle
        {
            public float X, Y, VX, VY, Size, Angle, Spin, Sway, Phase;
            public int Shade;
            public string Kind;
        }

        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Random _random = new Random();
        private readonly Forms.Timer _timer = new Forms.Timer { Interval = 33 };
        private DateTime _last = DateTime.UtcNow;

        private string _type = EffectType.None;
        private int _baseCount;
        private string _burstType = EffectType.Snow;
        private DateTime _burstUntil = DateTime.MinValue;
        private double _spawnDebt;

        private static readonly Drawing.Brush[] SnowBrushes =
        {
            new Drawing.SolidBrush(Drawing.Color.FromArgb(150, 255, 255, 255)),
            new Drawing.SolidBrush(Drawing.Color.FromArgb(185, 255, 255, 255)),
            new Drawing.SolidBrush(Drawing.Color.FromArgb(215, 255, 255, 255)),
            new Drawing.SolidBrush(Drawing.Color.FromArgb(242, 255, 255, 255))
        };
        private static readonly Drawing.Brush[] PetalBrushes =
        {
            new Drawing.SolidBrush(Drawing.Color.FromArgb(230, 249, 168, 212)),
            new Drawing.SolidBrush(Drawing.Color.FromArgb(230, 244, 114, 182)),
            new Drawing.SolidBrush(Drawing.Color.FromArgb(220, 253, 205, 225)),
            new Drawing.SolidBrush(Drawing.Color.FromArgb(220, 255, 228, 236))
        };
        private static readonly Drawing.Pen[] RainPens =
        {
            new Drawing.Pen(Drawing.Color.FromArgb(120, 200, 220, 255), 1.3f),
            new Drawing.Pen(Drawing.Color.FromArgb(160, 210, 228, 255), 1.6f),
            new Drawing.Pen(Drawing.Color.FromArgb(100, 190, 210, 245), 1.1f)
        };

        /// <summary>Có thay đổi cần vẽ lại.</summary>
        public event Action? Changed;

        /// <summary>Không còn hạt nào và không còn đợt thả → có thể ẩn lớp hiệu ứng.</summary>
        public event Action? Idle;

        /// <summary>Kích thước vùng vẽ (pixel).</summary>
        public int Width { get; set; }
        public int Height { get; set; }

        public ParticleEngine()
        {
            _timer.Tick += (_, __) => Step();
        }

        public bool IsActive => _timer.Enabled;

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
            Changed?.Invoke();
        }

        private static int BaseCount(string type, string intensity)
        {
            var factor = intensity == EffectIntensity.Light ? 0.5 : intensity == EffectIntensity.Heavy ? 1.8 : 1.0;
            var n = type == EffectType.Rain ? 160 : type == EffectType.Petals ? 45 : 110;
            return (int)(n * factor);
        }

        private void EnsureRunning()
        {
            if (_timer.Enabled) return;
            _last = DateTime.UtcNow;
            _timer.Start();
        }

        private void Step()
        {
            var now = DateTime.UtcNow;
            var dt = (float)Math.Min(0.1, (now - _last).TotalSeconds);
            _last = now;
            float w = Width, h = Height;
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
                    _particles.Add(Spawn(kind, w));
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
                p.X += (p.VX + (float)Math.Sin(p.Phase * 1.3 + p.Sway) * p.Sway * 12) * dt;
                p.Y += p.VY * dt;
                p.Angle += p.Spin * dt;
                if (p.Y > h + 30 || p.X < -40 || p.X > w + 40)
                {
                    // Hạt rơi khỏi màn hình: tái sinh nếu còn cần, không thì bỏ.
                    if (_particles.Count <= target) _particles[i] = Spawn(p.Kind, w);
                    else _particles.RemoveAt(i);
                    continue;
                }
                _particles[i] = p;
            }

            Changed?.Invoke();

            if (_particles.Count == 0 && target == 0)
            {
                _timer.Stop();
                Idle?.Invoke();
            }
        }

        private Particle Spawn(string kind, float w)
        {
            var p = new Particle
            {
                Kind = kind,
                X = (float)(_random.NextDouble() * w),
                Y = (float)(-20 - _random.NextDouble() * 40),
                Phase = (float)(_random.NextDouble() * 10),
                Shade = _random.Next(4)
            };
            switch (kind)
            {
                case EffectType.Rain:
                    p.Size = (float)(14 + _random.NextDouble() * 18);
                    p.VY = (float)(650 + _random.NextDouble() * 350);
                    p.VX = -60;
                    break;
                case EffectType.Petals:
                    p.Size = (float)(6 + _random.NextDouble() * 7);
                    p.VY = (float)(45 + _random.NextDouble() * 55);
                    p.VX = (float)(15 + _random.NextDouble() * 25);
                    p.Sway = (float)(2 + _random.NextDouble() * 3);
                    p.Spin = (float)((_random.NextDouble() - 0.5) * 160);
                    p.Angle = (float)(_random.NextDouble() * 360);
                    break;
                default:
                    p.Size = (float)(1.5 + _random.NextDouble() * 3.5);
                    p.VY = (float)(35 + _random.NextDouble() * 70);
                    p.VX = (float)((_random.NextDouble() - 0.5) * 20);
                    p.Sway = (float)(0.5 + _random.NextDouble() * 1.5);
                    break;
            }
            return p;
        }

        public void Render(Drawing.Graphics g)
        {
            if (_particles.Count == 0) return;
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            foreach (var p in _particles)
            {
                switch (p.Kind)
                {
                    case EffectType.Rain:
                        g.DrawLine(RainPens[p.Shade % RainPens.Length], p.X, p.Y, p.X + p.VX * 0.02f, p.Y + p.Size);
                        break;
                    case EffectType.Petals:
                        var state = g.Save();
                        g.TranslateTransform(p.X, p.Y);
                        g.RotateTransform(p.Angle);
                        g.FillEllipse(PetalBrushes[p.Shade % PetalBrushes.Length], -p.Size, -p.Size * 0.55f, p.Size * 2, p.Size * 1.1f);
                        g.Restore(state);
                        break;
                    default:
                        g.FillEllipse(SnowBrushes[p.Shade % SnowBrushes.Length], p.X - p.Size, p.Y - p.Size, p.Size * 2, p.Size * 2);
                        break;
                }
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
