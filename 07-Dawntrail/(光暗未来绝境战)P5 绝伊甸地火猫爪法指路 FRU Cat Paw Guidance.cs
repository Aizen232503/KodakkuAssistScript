using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using KodakkuAssist.Module.Draw;
using KodakkuAssist.Module.Draw.Manager;
using KodakkuAssist.Module.GameEvent;
using KodakkuAssist.Script;
using Newtonsoft.Json;

namespace VanivilleKalosScript;

[ScriptType(
    guid: "6A92C840-F7F6-4269-9CCA-F55E00E644C9",
    name: "(光暗未来绝境战)P5 绝伊甸地火猫爪法指路 FRU Cat Paw Guidance",
    territorys: [1238],
    version: "0.0.0.7",
    note: Notes,
    author: "Aizen232503 卡璞·仙仙"
)]
public class FuturesRewrittenUltimateCatPaw
{
    private const string Notes =
        "提供 P5 光尘之剑（地火）的猫爪法指路，与 MMW 攻略一致。移动次数较少，对部分职业较为友好。\n" +
        "原理说明：猫爪法的整体路线为三角形，沿顺时针或逆时针依次穿三次，最后回到起点。\n" +
        "绘制说明：默认显示三个点位圈的范围，以及当前点位和下一点位的指路。当前点位的箭头和点位圈默认绿色；下一点位的箭头和点位圈默认黄色。横幅提示中的穿入方向，指的是面向当前轮次地火（或猫爪三角形的中心）时穿猫爪的左／右，与 MMW 攻略一致，并非面向 Boss 的左右。\n" +
        "温馨提示：使用其他绝伊甸脚本时，请关闭其中重复的 P5 地火指路。例如，灵视脚本中的“Phase5 Guidance Of Fulgent Blade 璀璨之刃(地火)指路”功能。\n" +
        "特别鸣谢：洛可利亚奏鸣曲、Cicero 灵视";

    [UserSetting("当前点指路颜色")]
    public ScriptColor CurrentStepColour { get; set; } = new() { V4 = new(0f, 1f, 0f, 1f) };
    [UserSetting("下一点预览颜色")]
    public ScriptColor NextStepColour { get; set; } = new() { V4 = new(1f, 1f, 0f, 1f) };

    [UserSetting("全部点位圈颜色")]
    public ScriptColor PointCircleColour { get; set; } = new() { V4 = new(0f, 0.8f, 1f, 0.6f) };
    [UserSetting("始终显示猫爪法的三个点位圈")]
    public bool ShowPointCircles { get; set; } = true;
    [UserSetting("提示穿入方向\n即 MMW 攻略里穿猫爪的左／右，以面向当前轮次地火（或猫爪三角形的中心）时的左右为准，并非面向 Boss 的左右。\n建议仅在开启“始终显示猫爪法的三个点位圈”时启用。")]
    public bool ShowEntryDirection { get; set; } = true;

    private int fireRound;
    private string phase = "";
    private readonly List<Blade> blades = [];
    private readonly object drawLock = new();
    private Blade[] firstAndLastBlades = [];
    private Vector2 point1, point2, point3, middlePoint;
    private CatPawRoute? catPawRoute;
    private readonly HashSet<string> handledMethods = [];

    private record CatPawRoute(uint GlowId, Vector2[] Points, string Side, bool IsInverse);

    private record Blade(uint Id, double X, double Y, double Rotation);

    private void ResetStates()
    {
        // 更换地火轮次，停止上一轮尚未显示的横幅。
        fireRound++;
        phase = "";
        blades.Clear();
        firstAndLastBlades = [];
        catPawRoute = null;
        handledMethods.Clear();
    }

    public void Init(ScriptAccessory accessory)
    {
        lock (drawLock)
        {
            ResetStates();
            accessory.Method.RemoveDraw("^FRUPatch_CatPaw_");
        }
    }

    [ScriptMethod(name: "P5地火开始", eventType: EventTypeEnum.StartCasting,
        eventCondition: ["ActionId:40306"], userControl: false)]
    public void FulgentBladeStart(Event @event, ScriptAccessory accessory)
    {
        lock (drawLock)
        {
            ResetStates();
            phase = "P5地火";
        }
    }

    [ScriptMethod(name: "P5地火数据捕获", eventType: EventTypeEnum.ObjectEffect,
        eventCondition: ["Id1:1"], userControl: false)]
    public void CaptureBlades(Event @event, ScriptAccessory accessory)
    {
        lock (drawLock)
        {
            if (phase != "P5地火") return;
            var position = JsonConvert.DeserializeObject<Vector3>(@event["SourcePosition"]);
            blades.Add(new Blade(Convert.ToUInt32(@event["SourceId"], 16),
                position.X, position.Z, Convert.ToDouble(@event["SourceRotation"])));
            if (blades.Count == 6) ProcessBlades();
        }
    }

    private void ProcessBlades()
    {
        // 按实体 ID 排序，两两计算三个交点；第一、第三交点的中点为处理中心。
        var sorted = blades.OrderBy(blade => blade.Id).ToArray();
        firstAndLastBlades = [sorted[0], sorted[1], sorted[4], sorted[5]];
        point1 = GetIntersection(sorted[0], sorted[1]);
        point2 = GetIntersection(sorted[2], sorted[3]);
        point3 = GetIntersection(sorted[4], sorted[5]);
        middlePoint = (point1 + point3) / 2;
        phase = "P5地火计算完成";
    }

    [ScriptMethod(name: "光尘之剑（地火）猫爪法指路（MMW）", eventType: EventTypeEnum.ObjectEffect,
        eventCondition: ["Id2:16"])]
    public void CatPawGuidance(Event @event, ScriptAccessory accessory)
    {
        lock (drawLock)
        {
            var data = PrepareCatPawRoute(@event, nameof(CatPawGuidance));
            if (data is null) return;
            var route = data.Points;

            // 从首个发光事件计时：9 秒去 B，再每隔 4 秒去 C、回 A，最后显示 2 秒。
            int[] delays = [0, 9000, 13000, 17000];
            int[] durations = [9000, 4000, 4000, 2000];
            if (ShowPointCircles)
            {
                // 三个点位圈持续显示到最后一次回 A 的指路结束。
                for (int point = 0; point < 3; point++)
                    DrawPointCircle(accessory, $"Point_{(char)('A' + point)}", route[point],
                        1.00f, 0.90f, PointCircleColour.V4, 0, 19000);
            }
            for (int step = 0; step < route.Length; step++)
            {
                DrawPointCircle(accessory, $"ActivePoint_{step}", route[step],
                    1.05f, 0.95f, CurrentStepColour.V4, delays[step], durations[step]);

                // 当前点箭头从玩家指向当前点，预览箭头固定从当前点指向下一点。
                var current = accessory.Data.GetDefaultDrawProperties();
                current.Name = $"FRUPatch_CatPaw_Current_{step}";
                current.Owner = accessory.Data.Me;
                current.TargetPosition = new(route[step].X, 0, route[step].Y);
                current.Scale = new(2);
                current.ScaleMode |= ScaleMode.YByDistance;
                current.Color = CurrentStepColour.V4;
                current.Delay = delays[step];
                current.DestoryAt = durations[step];
                accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, current);

                if (step == route.Length - 1) continue;
                DrawPointCircle(accessory, $"NextPoint_{step}", route[step + 1],
                    1.05f, 0.95f, NextStepColour.V4, delays[step], durations[step]);

                var next = accessory.Data.GetDefaultDrawProperties();
                next.Name = $"FRUPatch_CatPaw_Next_{step}";
                next.Position = new(route[step].X, 0, route[step].Y);
                next.TargetPosition = new(route[step + 1].X, 0, route[step + 1].Y);
                next.Scale = new(2);
                next.ScaleMode |= ScaleMode.YByDistance;
                next.Color = NextStepColour.V4;
                next.Delay = delays[step];
                next.DestoryAt = durations[step];
                accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, next);
            }
        }
    }

    [ScriptMethod(name: "光尘之剑（地火）起点横幅提示", eventType: EventTypeEnum.ObjectEffect,
        eventCondition: ["Id2:16"])]
    public void CatPawStartBanner(Event @event, ScriptAccessory accessory)
    {
        lock (drawLock)
        {
            var data = PrepareCatPawRoute(@event, nameof(CatPawStartBanner));
            if (data is null) return;
            string entry = ShowEntryDirection ? EntryDirection(data.IsInverse) : "按箭头穿入";
            accessory.Method.TextInfo($"场地{data.Side}侧开始，稍后{entry}", 5000);
        }
    }

    [ScriptMethod(name: "光尘之剑（地火）后续点横幅提示", eventType: EventTypeEnum.ObjectEffect,
        eventCondition: ["Id2:16"])]
    public void CatPawMoveBanner(Event @event, ScriptAccessory accessory)
    {
        lock (drawLock)
        {
            var data = PrepareCatPawRoute(@event, nameof(CatPawMoveBanner));
            if (data is null) return;
            _ = ShowMoveBanners(accessory, data.IsInverse, fireRound);
        }
    }

    // 首个有效发光事件确定共用路线，指路和两种横幅分别只执行一次。
    private CatPawRoute? PrepareCatPawRoute(Event @event, string method)
    {
        if (phase != "P5地火计算完成" && phase != "P5运算结束") return null;
        var id = Convert.ToUInt32(@event["SourceId"], 16);
        if (phase == "P5地火计算完成")
        {
            if (!firstAndLastBlades.Any(blade => blade.Id == id)) return null;
            catPawRoute = CalculateCatPawRoute(id);
            phase = "P5运算结束";
        }
        if (catPawRoute is null || id != catPawRoute.GlowId || !handledMethods.Add(method)) return null;
        return catPawRoute;
    }

    private CatPawRoute CalculateCatPawRoute(uint id)
    {
        // 根据交点中点的位置，确定本轮在场地哪一侧处理地火。
        Vector2[] centres = [new(100.000f, 92.929f), new(107.071f, 100.000f),
            new(100.000f, 107.071f), new(92.929f, 100.000f)];
        int centreIndex = Enumerable.Range(0, centres.Length)
            .OrderBy(index => Vector2.DistanceSquared(centres[index], middlePoint)).First();
        Vector2 centre = centres[centreIndex];

        // 发光实体决定首组交点，计算首组和第二组相对处理中心的八方向编号。
        Vector2 firstOffset = (id == firstAndLastBlades[0].Id || id == firstAndLastBlades[1].Id ? point1 : point3) - middlePoint;
        Vector2 secondOffset = point2 - middlePoint;
        int firstDirection = ((int)Math.Round((Math.Atan2(firstOffset.X, firstOffset.Y) / Math.PI + 1) * 4 - 0.5) + 8) % 8;
        int secondDirection = ((int)Math.Round((Math.Atan2(secondOffset.X, secondOffset.Y) / Math.PI + 1) * 4 - 0.5) + 8) % 8;
        // 第二组方向编号比首组增加 2 时为逆时针，否则为顺时针。
        bool isInverse = (secondDirection - firstDirection + 8) % 8 == 2;
        Vector2[] startOffsets =
        [
            new(0.518f, 1.250f), new(1.250f, 0.518f), new(1.250f, -0.518f), new(0.518f, -1.250f),
            new(-0.518f, -1.250f), new(-1.250f, -0.518f), new(-1.250f, 0.518f), new(-0.518f, 1.250f)
        ];
        Vector2[] nextOffsets =
        [
            new(-1.250f, -3.018f), new(-3.018f, -1.250f), new(-3.018f, 1.250f), new(-1.250f, 3.018f),
            new(1.250f, 3.018f), new(3.018f, 1.250f), new(3.018f, -1.250f), new(1.250f, -3.018f)
        ];
        // A 位于首组交点的对侧；B、C 取相邻两侧，按顺逆时针决定先后。
        Vector2 pointA = centre + startOffsets[firstDirection];
        Vector2 pointB = centre + nextOffsets[(firstDirection + (isInverse ? 7 : 1)) % 8];
        Vector2 pointC = centre + nextOffsets[(firstDirection + (isInverse ? 1 : 7)) % 8];
        string[] sides = ["上北", "右东", "下南", "左西"];
        return new CatPawRoute(id, [pointA, pointB, pointC, pointA], sides[centreIndex], isInverse);
    }

    private static string EntryDirection(bool isInverse) => isInverse ? "向右逆时针穿入" : "向左顺时针穿入";

    private async Task ShowMoveBanners(ScriptAccessory accessory, bool isInverse, int round)
    {
        // 三次换点与指路同步：首次等待 9 秒，后续每隔 4 秒。
        for (int step = 1; step < 4; step++)
        {
            await Task.Delay(step == 1 ? 9000 : 4000);
            lock (drawLock)
            {
                if (round != fireRound) return;
                accessory.Method.TextInfo(ShowEntryDirection ? EntryDirection(isInverse) : "穿入下一点", 2000);
            }
        }
    }

    private static void DrawPointCircle(ScriptAccessory accessory, string name, Vector2 point,
        float outerRadius, float innerRadius, Vector4 colour, int delay, int duration)
    {
        var circle = accessory.Data.GetDefaultDrawProperties();
        circle.Name = $"FRUPatch_CatPaw_{name}";
        circle.Position = new(point.X, 0, point.Y);
        circle.Scale = new(outerRadius);
        circle.InnerScale = new(innerRadius);
        circle.Radian = float.Pi * 2;
        circle.Color = colour;
        circle.Delay = delay;
        circle.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Donut, circle);
    }

    private static Vector2 GetIntersection(Blade first, Blade second)
    {
        // 地火实体朝向垂直于地板线，用两条线的方程求交点。
        float s1 = (float)Math.Sin(first.Rotation), c1 = (float)Math.Cos(first.Rotation);
        float s2 = (float)Math.Sin(second.Rotation), c2 = (float)Math.Cos(second.Rotation);
        float x1 = (float)first.X, y1 = (float)first.Y;
        float x2 = (float)second.X, y2 = (float)second.Y;
        float denominator = s1 * c2 - s2 * c1;
        return new(
            (x1 * s1 * c2 - x2 * s2 * c1 - (y2 - y1) * c1 * c2) / denominator,
            (y2 * c2 * s1 - y1 * c1 * s2 + (x2 - x1) * s1 * s2) / denominator);
    }
}
