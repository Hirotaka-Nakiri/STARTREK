using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STARTREK
{
    internal class Program
    {
        static readonly Random Rng = new Random();

        // TinyBASIC風: R.(n) ≈ 0..n-1 / 1..n
        static int R0(int n) { return (n <= 0) ? 0 : Rng.Next(n); }       // 0..n-1
        static int R1(int n) { return (n <= 0) ? 0 : (Rng.Next(n) + 1); } // 1..n

        static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        const int QSize = 8; // quadrant 8x8
        const int SSize = 8; // sector   8x8

        const char EMPTY = '.';
        const char KLINGON = 'K';
        const char STARBASE = 'B';
        const char STAR = '*';
        const char ENTERPRISE = 'E';

        static int stardatesLeft = 30;
        static int totalKlingons;
        static int totalStarbases;

        static int quadX, quadY;   // 0..7
        static int secX, secY;     // 0..7

        static int energy;
        static int torpedoes;

        // Damage device timers (0=OK): 1 SR, 2 Map, 3 LR, 4 Phaser, 5 Warp, 6 Torpedo, 7 Shield
        static int[] damage = new int[8];

        struct QuadrantInfo
        {
            public int Klingons;
            public int Starbases;
            public int Stars;
        }

        static QuadrantInfo[,] galaxy = new QuadrantInfo[QSize, QSize];
        static char[,] sector = new char[SSize, SSize];

        class KlingonObj
        {
            public int X, Y;
            public int HP;
        }

        static List<KlingonObj> klingonsHere = new List<KlingonObj>();

        static void Main(string[] args)
        {
            int cps = 30; // default
            if (args != null && args.Length >= 1)
            {
                int tmp;
                if (int.TryParse(args[0], out tmp) && tmp > 0)
                    cps = tmp;
            }

            // 安全側で制限（好きなら上限は外してOK）
            cps = Clamp(cps, 1, 200);

            // ★出力をテレタイプ風に遅延させる
            Console.SetOut(new ThrottledTextWriter(Console.Out, cps));

            while (true)
            {
                if (!StartNewGame()) break;

                GameLoop();

                Console.WriteLine();
                Console.Write("ANOTHER GAME (Y OR N)? ");
                bool again = ReadYesNo();
                if (!again) break;
            }

            Console.WriteLine("GOOD BYE.");
        }

        static bool StartNewGame()
        {
            Console.Write("DO YOU WANT A DIFFICULT GAME (Y OR N)? ");
            bool difficult = ReadYesNo();

            Console.WriteLine("STARDATE 3200  YOUR MISSION IS ...");

            // ざっくり難易度
            int difficultyFactor = difficult ? 1 : 2;

            totalKlingons = R1(8) + R1(8) + (difficult ? R1(8) : 0);
            totalKlingons = Clamp(totalKlingons / difficultyFactor, 6, 24);

            totalStarbases = Clamp(R1(5), 2, 6);

            stardatesLeft = 30;
            energy = 4000;
            torpedoes = 10;
            Array.Clear(damage, 0, damage.Length);

            for (int y = 0; y < QSize; y++)
                for (int x = 0; x < QSize; x++)
                    galaxy[x, y] = new QuadrantInfo { Klingons = 0, Starbases = 0, Stars = 0 };

            DistributeAcrossGalaxy(totalKlingons, totalStarbases);

            // Enterprise start
            quadX = R0(8); quadY = R0(8);
            secX = R0(8); secY = R0(8);

            EnterQuadrant(quadX, quadY, true); // 既定: 画面表示あり / E配置あり

            Console.WriteLine("TO DESTROY {0} KLINGONS IN 30 STARDATES.", totalKlingons);
            Console.WriteLine("THERE ARE {0} STARBASES.", totalStarbases);

            return true;
        }

        static void DistributeAcrossGalaxy(int klingons, int bases)
        {
            // Klingons
            for (int i = 0; i < klingons; i++)
            {
                int x = R0(8), y = R0(8);
                QuadrantInfo q = galaxy[x, y];
                q.Klingons++;
                galaxy[x, y] = q;
            }

            // Starbases
            for (int i = 0; i < bases; i++)
            {
                int x, y;
                do { x = R0(8); y = R0(8); } while (galaxy[x, y].Starbases > 0);

                QuadrantInfo q = galaxy[x, y];
                q.Starbases = 1;
                galaxy[x, y] = q;
            }

            // Stars
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    QuadrantInfo q = galaxy[x, y];
                    q.Stars = R1(8);
                    galaxy[x, y] = q;
                }
        }

        static void GameLoop()
        {
            while (true)
            {
                if (totalKlingons <= 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("MISSION ACCOMPLISHED.");
                    return;
                }

                if (stardatesLeft <= 0)
                {
                    Console.WriteLine("IT'S TOO LATE, THE FEDERATION HAS BEEN CONQUERED.");
                    return;
                }

                if (energy <= 0)
                {
                    Console.WriteLine("ENTERPRISE DESTROYED");
                    return;
                }

                RepairTick();

                if (IsDocked()) //docked
                {
                    energy = 4000;
                    torpedoes = 10;
                }

                Console.WriteLine();
                Console.Write("COMMAND (R,S,L,G,P,T,W): ");
                string cmd = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();
                if (cmd.Length == 0) continue;

                char c = cmd[0];

                if (!IsDeviceOperational(c))
                {
                    Console.WriteLine("DEVICE DAMAGED.");
                    continue;
                }

                switch (c)
                {
                    case 'R': StatusReport(); break;         // report
                    case 'S': ShortRangeSensor(); break;     // SR sensor
                    case 'L': LongRangeSensor(); break;      // LR sensor
                    case 'G': GalaxyMap(); break;            // galaxy map
                    case 'P': FirePhaser(); break;           // phaser
                    case 'T': FireTorpedoClassic(); break;   // torpedo (classic course)
                    case 'W': WarpClassic(); break;          // warp factor (classic)
                    default:
                        Console.WriteLine("PLEASE USE ONE OF THESE COMMANDS: R,S,L,G,P,T,W");
                        break;
                }

                KlingonAttackPhase();
            }
        }

        static bool ReadYesNo()
        {
            while (true)
            {
                string s = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();
                if (s.StartsWith("Y")) return true;
                if (s.StartsWith("N")) return false;
                Console.Write("Y OR N? ");
            }
        }

        // ----- Devices / Damage -----

        static string DeviceName(int d)
        {
            switch (d)
            {
                case 1: return "SHORT RANGE SENSOR";
                case 2: return "COMPUTER DISPLAY";
                case 3: return "LONG RANGE SENSOR";
                case 4: return "PHASER";
                case 5: return "WARP ENGINE";
                case 6: return "PHOTON TORPEDO TUBES";
                case 7: return "SHIELD";
                default: return "UNKNOWN";
            }
        }

        static bool IsDeviceOperational(char cmd)
        {
            int dev = 0;
            switch (cmd)
            {
                case 'S': dev = 1; break;
                case 'G': dev = 2; break;
                case 'L': dev = 3; break;
                case 'P': dev = 4; break;
                case 'W': dev = 5; break;
                case 'T': dev = 6; break;
                case 'R': dev = 0; break;
                default: dev = 0; break;
            }

            if (dev == 0) return true;
            return damage[dev] <= 0;
        }

        static void RepairTick()
        {
            for (int i = 1; i <= 7; i++)
                if (damage[i] > 0) damage[i]--;

            // ダメージ量をランダムで決定
            if (R0(100) < 5)
            {
                int dev = R1(7);
                damage[dev] += R1(6);
                Console.WriteLine();
                Console.WriteLine("DAMAGE REPORT:");
                Console.WriteLine("{0} DAMAGED, {1} STARDATES ESTIMATED FOR REPAIR", DeviceName(dev), damage[dev]);
            }
        }

        // ----- Docking -----

        static bool IsDocked()
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = secX + dx, y = secY + dy;
                    if (x < 0 || x >= 8 || y < 0 || y >= 8) continue;
                    if (sector[x, y] == STARBASE) return true;
                }
            return false;
        }

        // ----- Galaxy/Sector enter & placement -----
        static void EnterQuadrant(int qx, int qy, bool keepEnterprisePosIfPossible, bool placeEnterprise = true, bool showMessage = true)
        {
            klingonsHere.Clear();

            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    sector[x, y] = EMPTY;

            int stars = galaxy[qx, qy].Stars;
            PlaceRandomObjects(stars, STAR);

            if (galaxy[qx, qy].Starbases > 0)
                PlaceRandomObjects(1, STARBASE);

            int k = galaxy[qx, qy].Klingons;
            for (int i = 0; i < k; i++)
            {
                var pos = PlaceRandomObjects(1, KLINGON);
                klingonsHere.Add(new KlingonObj { X = pos.x, Y = pos.y, HP = 200 + R0(301) });
            }

            if (placeEnterprise)
            {
                if (!keepEnterprisePosIfPossible || sector[secX, secY] != EMPTY)
                {
                    while (true)
                    {
                        int x = R0(8), y = R0(8);
                        if (sector[x, y] == EMPTY) { secX = x; secY = y; break; }
                    }
                }
                sector[secX, secY] = ENTERPRISE;
            }

            if (showMessage && placeEnterprise)
            {
                Console.WriteLine();
                Console.WriteLine("ENTERPRISE IN Q-{0},{1}  S-{2},{3}", qx + 1, qy + 1, secX + 1, secY + 1);
            }
        }

        static (int x, int y) PlaceRandomObjects(int count, char obj)
        {
            int lx = 0, ly = 0;

            for (int i = 0; i < count; i++)
            {
                while (true)
                {
                    int x = R0(8), y = R0(8);
                    if (sector[x, y] == EMPTY)
                    {
                        sector[x, y] = obj;
                        lx = x; ly = y;
                        break;
                    }
                }
            }

            return (lx, ly);
        }

        // ----- Displays -----

        static void StatusReport()
        {
            Console.WriteLine("STATUS REPORT");
            Console.WriteLine("STARDATE {0}", 3230 - stardatesLeft);
            Console.WriteLine("TIME LEFT {0}", stardatesLeft);

            Console.Write("CONDITION ");
            if (IsDocked()) Console.WriteLine("DOCKED");
            else if (klingonsHere.Count > 0) Console.WriteLine("RED");
            else if (energy < 1000) Console.WriteLine("YELLOW");
            else Console.WriteLine("GREEN");

            Console.WriteLine("POSITION Q-{0},{1}  S-{2},{3}", quadX + 1, quadY + 1, secX + 1, secY + 1);
            Console.WriteLine("ENERGY {0}", energy);
            Console.WriteLine("TORPEDOES {0}", torpedoes);
            Console.WriteLine("KLINGONS LEFT {0}", totalKlingons);
            Console.WriteLine("STARBASES {0}", totalStarbases);

            for (int i = 1; i <= 7; i++)
                if (damage[i] > 0)
                    Console.WriteLine("{0} DAMAGED, {1} STARDATES ESTIMATED FOR REPAIR", DeviceName(i), damage[i]);
        }

        static void ShortRangeSensor()
        {
            Console.WriteLine();
            for (int y = 0; y < 8; y++)
            {
                Console.Write("{0} ", y + 1);
                for (int x = 0; x < 8; x++)
                {
                    Console.Write(sector[x, y]);
                    Console.Write(' ');
                }
                Console.WriteLine();
            }
            Console.Write("  ");
            for (int x = 0; x < 8; x++) Console.Write("{0} ", x + 1);
            Console.WriteLine();
        }

        static void LongRangeSensor()
        {
            Console.WriteLine();
            for (int qy = quadY - 1; qy <= quadY + 1; qy++)
            {
                for (int qx = quadX - 1; qx <= quadX + 1; qx++)
                {
                    if (qx < 0 || qx >= 8 || qy < 0 || qy >= 8)
                    {
                        Console.Write(" *** ");
                        continue;
                    }

                    QuadrantInfo qi = galaxy[qx, qy];
                    Console.Write(" {0}{1}{2} ", qi.Klingons, qi.Starbases, qi.Stars); // KBS
                }
                Console.WriteLine();
            }
        }

        static void GalaxyMap()
        {
            Console.WriteLine();
            Console.WriteLine("GALAXY MAP (KBS)");
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    QuadrantInfo qi = galaxy[x, y];
                    Console.Write("{0}{1}{2} ", qi.Klingons, qi.Starbases, qi.Stars);
                }
                Console.WriteLine();
            }
        }

        // ----- Classic Course (1.0 - 9.0) -----
        // 1:上, 2:右上, 3:右, 4:右下, 5:下, 6:左下, 7:左, 8:左上, 9=1
        // 画面座標: x右+, y下+ なので「上」は dy=-1
        static bool ReadCourseClassic(out double dx, out double dy)
        {
            dx = 0; dy = 0;

            Console.Write("COURSE (1.0-9.0)? ");
            double c;
            if (!double.TryParse((Console.ReadLine() ?? "").Trim(), out c)) return false;

            if (c < 1.0 || c > 9.0) return false;
            if (Math.Abs(c - 9.0) < 1e-12) c = 1.0;

            double[] cx = { 0, 0, 1, 1, 1, 0, -1, -1, -1, 0 };
            double[] cy = { 0, -1, -1, 0, 1, 1, 1, 0, -1, 0 };

            int c1 = (int)Math.Floor(c);
            double frac = c - c1;

            int c2 = c1 + 1;
            if (c2 == 9) c2 = 1;

            // 小数分を隣方向へ補間
            dx = cx[c1] + (cx[c2] - cx[c1]) * frac;
            dy = cy[c1] + (cy[c2] - cy[c1]) * frac;

            // 正規化
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 1e-9)
            {
                dx /= len;
                dy /= len;
            }

            return true;
        }

        // ----- Weapons -----

        static void FirePhaser()
        {
            Console.Write("ENERGY UNITS TO FIRE? ");
            int a;
            if (!int.TryParse(Console.ReadLine(), out a) || a <= 0)
            {
                Console.WriteLine("CANCELLED.");
                return;
            }

            if (a > energy)
            {
                Console.WriteLine("SPOCK: WE HAVE ONLY {0} UNITS.", energy);
                return;
            }

            energy -= a;

            if (klingonsHere.Count == 0)
            {
                Console.WriteLine("PHASER FIRED AT EMPTY SPACE.");
                return;
            }

            int per = Math.Max(1, a / klingonsHere.Count);

            for (int i = klingonsHere.Count - 1; i >= 0; i--)
            {
                KlingonObj k = klingonsHere[i];
                int hit = per + R0(per + 1);
                k.HP -= hit;

                Console.WriteLine("{0} UNITS HIT KLINGON AT S-{1},{2}", hit, k.X + 1, k.Y + 1);

                if (k.HP <= 0)
                {
                    Console.WriteLine("DESTROYED");
                    sector[k.X, k.Y] = EMPTY;
                    klingonsHere.RemoveAt(i);

                    QuadrantInfo q = galaxy[quadX, quadY];
                    q.Klingons--;
                    galaxy[quadX, quadY] = q;

                    totalKlingons--;
                }
            }
        }

        static void FireTorpedoClassic()
        {
            if (torpedoes <= 0)
            {
                Console.WriteLine("EMPTY");
                return;
            }

            Console.WriteLine("LOADED");

            double vx, vy;
            if (!ReadCourseClassic(out vx, out vy)) return;

            torpedoes--;

            // 1セクターを8分割
            double step = 1.0 / 8.0;
            double px = secX;
            double py = secY;

            int lastPrintX = -999, lastPrintY = -999;

            while (true)
            {
                px += vx * step;
                py += vy * step;

                int x = (int)Math.Round(px);
                int y = (int)Math.Round(py);

                // 範囲外ならミス
                if (x < 0 || x > 7 || y < 0 || y > 7)
                {
                    Console.WriteLine("...MISSED");
                    return;
                }

                // ★重要：自艦(E)のマスは無視（Roundの都合でしばらく同じセルになるため）
                if (x == secX && y == secY)
                    continue;

                // 同じセルを何度も表示しない（見やすさ改善）
                if (x != lastPrintX || y != lastPrintY)
                {
                    Console.WriteLine("TORPEDO TRACK {0},{1}", x + 1, y + 1);
                    lastPrintX = x;
                    lastPrintY = y;
                }

                char cell = sector[x, y];
                if (cell == EMPTY) continue;

                if (cell == KLINGON)
                {
                    Console.WriteLine("KLINGON DESTROYED");
                    sector[x, y] = EMPTY;

                    for (int i = 0; i < klingonsHere.Count; i++)
                    {
                        if (klingonsHere[i].X == x && klingonsHere[i].Y == y)
                        {
                            klingonsHere.RemoveAt(i);
                            break;
                        }
                    }

                    QuadrantInfo q = galaxy[quadX, quadY];
                    q.Klingons--;
                    galaxy[quadX, quadY] = q;

                    totalKlingons--;
                    return;
                }

                if (cell == STARBASE)
                {
                    Console.WriteLine("STARBASE DESTROYED");
                    sector[x, y] = EMPTY;

                    QuadrantInfo q = galaxy[quadX, quadY];
                    q.Starbases--;
                    galaxy[quadX, quadY] = q;

                    totalStarbases--;
                    return;
                }

                if (cell == STAR)
                {
                    Console.WriteLine("HIT A STAR");
                    if (R0(9) < 6)
                    {
                        Console.WriteLine("STAR DESTROYED");
                        sector[x, y] = EMPTY;

                        QuadrantInfo q = galaxy[quadX, quadY];
                        q.Stars--;
                        galaxy[quadX, quadY] = q;
                    }
                    return;
                }

                // 想定外のセル（ここには来ないかも）
                Console.WriteLine("...STOPPED");
                return;
            }
        }

        // ----- Warp (Classic: Warp Factor) -----
        static void WarpClassic()
        {
            Console.Write("WARP FACTOR (0-8)? ");
            double wf;
            if (!double.TryParse((Console.ReadLine() ?? "").Trim(), out wf)) return;
            if (wf <= 0.0) return;
            if (wf > 8.0) wf = 8.0;

            // 移動距離（セクター数）
            double distSectors = wf * 8.0;

            // 消費 = (距離^2) / 2
            int cost = (int)Math.Round((distSectors * distSectors) / 2.0);

            if (energy < cost)
            {
                Console.WriteLine("SCOTTY: WE DO NOT HAVE THE ENERGY.");
                return;
            }

            double vx, vy;
            if (!ReadCourseClassic(out vx, out vy)) return;

            stardatesLeft -= 1;
            energy -= cost;

            // 今居る位置の E を消す（以降、ワープ中はEを置かずに障害物チェックする）
            sector[secX, secY] = EMPTY;

            // グローバル座標（銀河全体を 64x64 セクターとみなす）
            double gx = quadX * 8 + secX;
            double gy = quadY * 8 + secY;

            // 最後に戻れる安全地点（整数グローバル）
            int lastSafeGX = (int)Math.Round(gx);
            int lastSafeGY = (int)Math.Round(gy);

            // 1セクターを8分割で移動
            double step = 1.0 / 8.0;
            int steps = (int)Math.Ceiling(distSectors / step);

            // 「いまロードされているクアドラント」
            int loadedQX = quadX;
            int loadedQY = quadY;

            for (int i = 0; i < steps; i++)
            {
                gx += vx * step;
                gy += vy * step;

                int igx = (int)Math.Round(gx);
                int igy = (int)Math.Round(gy);

                // 銀河外チェック（0..63）
                if (igx < 0 || igx > 63 || igy < 0 || igy > 63)
                {
                    Console.WriteLine("YOU WANDERED OUTSIDE THE GALAXY");
                    Console.WriteLine("ON BOARD COMPUTER TAKES OVER, AND SAVED YOUR LIFE");

                    quadX = R0(8); quadY = R0(8);
                    secX = R0(8); secY = R0(8);
                    EnterQuadrant(quadX, quadY, true, placeEnterprise: true, showMessage: true);
                    return;
                }

                int nqX = igx / 8;
                int nqY = igy / 8;
                int nsX = igx % 8;
                int nsY = igy % 8;

                // クアドラントが変わったら、そのクアドラントのマップをロードして続行（止まらない）
                if (nqX != loadedQX || nqY != loadedQY)
                {
                    quadX = nqX; quadY = nqY;
                    secX = nsX; secY = nsY;

                    // ワープ中 E は置かない / 表示もしない（マップだけ更新）
                    EnterQuadrant(quadX, quadY, true, placeEnterprise: false, showMessage: false);

                    loadedQX = nqX;
                    loadedQY = nqY;
                }
                else
                {
                    // 同一クアドラント内なら位置更新のみ
                    quadX = nqX; quadY = nqY;
                    secX = nsX; secY = nsY;
                }

                // 障害物判定（空以外なら緊急停止 → 直前の安全地点で止める）
                if (sector[secX, secY] != EMPTY)
                {
                    Console.WriteLine("EMERGENCY STOP");
                    Console.WriteLine("SPOCK: TO ERR IS HUMAN.");
                    break;
                }

                lastSafeGX = igx;
                lastSafeGY = igy;
            }

            // 最終位置（安全地点）に確定
            quadX = lastSafeGX / 8;
            quadY = lastSafeGY / 8;
            secX = lastSafeGX % 8;
            secY = lastSafeGY % 8;

            // 最終クアドラントをロードし、Enterprise を置いて終了
            EnterQuadrant(quadX, quadY, true, placeEnterprise: true, showMessage: true);
        }

        // ----- Klingon Attack -----

        static void KlingonAttackPhase()
        {
            if (klingonsHere.Count == 0) return;

            Console.WriteLine();
            Console.WriteLine("KLINGON ATTACK");

            if (IsDocked())
            {
                Console.WriteLine("STARBASE PROTECTS ENTERPRISE");
                return;
            }

            int totalHit = 0;

            foreach (KlingonObj k in klingonsHere)
            {
                int dx = k.X - secX;
                int dy = k.Y - secY;
                int dist2 = dx * dx + dy * dy;

                int baseHit = 200 + R0(200);
                int hit = Math.Max(1, baseHit / Math.Max(1, dist2));

                totalHit += hit;
                Console.WriteLine("{0} UNITS HIT FROM KLINGON AT S-{1},{2}", hit, k.X + 1, k.Y + 1);
            }

            energy -= totalHit;

            if (energy <= 0)
            {
                Console.WriteLine("BANG");
                return;
            }

            Console.WriteLine("{0} UNITS OF ENERGY LEFT.", energy);
        }
    }
    //画面表示をレトロ風に1キャラクタ単位で表示させるクラス
    sealed class ThrottledTextWriter : System.IO.TextWriter
    {
        private readonly System.IO.TextWriter _inner;
        private readonly double _cps;
        private long _nextTicks; // 次に出力可能な時刻（Stopwatch ticks）

        public ThrottledTextWriter(System.IO.TextWriter inner, int charsPerSecond)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cps = Math.Max(1.0, charsPerSecond);
            _nextTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            // CR は文字数にカウントせず即出力（改行が遅すぎるのを防ぐ）
            if (value == '\r')
            {
                _inner.Write(value);
                return;
            }

            ThrottleOneChar();
            _inner.Write(value);

            // 改行はすぐ画面に反映したいのでフラッシュ
            if (value == '\n') _inner.Flush();
        }

        public override void Write(string value)
        {
            if (value == null) return;
            for (int i = 0; i < value.Length; i++)
                Write(value[i]);
        }

        public override void WriteLine(string value)
        {
            Write(value);
            Write('\n');
        }

        public override void WriteLine()
        {
            Write('\n');
        }

        private void ThrottleOneChar()
        {
            // 1文字あたりの間隔
            long freq = System.Diagnostics.Stopwatch.Frequency;
            long interval = (long)(freq / _cps);

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now < _nextTicks)
            {
                double ms = (_nextTicks - now) * 1000.0 / freq;
                if (ms > 1.0) System.Threading.Thread.Sleep((int)ms);
                else System.Threading.Thread.Sleep(0);
            }

            // 次の文字の出力タイマー
            long now2 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now2 > _nextTicks) _nextTicks = now2;
            _nextTicks += interval;
        }
    }

}
