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
    version: "0.0.0.8",
    note: Notes,
    author: "Aizen232503 卡璞·仙仙"
)]
public class FuturesRewrittenUltimateCatPaw
{
    private const string Notes =
        "提供 P5 光尘之剑（地火）的猫爪法指路，与 MMW 攻略一致。移动次数较少，对部分职业较为友好。同时，地火指路时机可比特效出现提前至多 7 秒以供提前就位。\n" +
        "原理说明：猫爪法的整体路线为三角形，沿顺时针或逆时针依次穿三次，最后回到起点。\n" +
        "绘制说明：默认显示三个点位圈的范围，以及当前点位和下一点位的指路。当前点位的箭头和点位圈默认绿色；下一点位的箭头和点位圈默认黄色。指路默认在“光尘之剑”读条开始 8 秒后（地火特效出现时）显示，可在用户配置中调整。起点横幅提示固定在读条开始 10 秒后（首组地火亮起时）显示。横幅提示中的穿入方向，指的是面向当前轮次地火（或猫爪三角形的中心）时穿猫爪的左／右，与 MMW 攻略一致，并非面向 Boss 的左右。\n" +
        "温馨提示：使用其他绝伊甸脚本时，请关闭其中重复的 P5 地火指路。例如，灵视脚本中的“Phase5 Guidance Of Fulgent Blade 璀璨之刃(地火)指路”功能。\n" +
        "特别鸣谢：洛可利亚奏鸣曲、Cicero 灵视";

    private float guidanceDelaySeconds = 8f;
    [UserSetting("“光尘之剑”读条开始后多久显示指路（秒，1～12），默认为 8 秒。\n在常人眼里，读条开始后约 8 秒才会出现地火特效；然而，在灵视的加持下，你也可以只需 1 秒。")]
    public float GuidanceDelaySeconds
    {
        get => guidanceDelaySeconds;
        set => guidanceDelaySeconds = Math.Clamp(value, 1f, 12f);
    }

    [UserSetting("当前点指路颜色")]
    public ScriptColor CurrentStepColour { get; set; } = new() { V4 = new(0f, 1f, 0f, 1f) };
    [UserSetting("下一点预览颜色")]
    public ScriptColor NextStepColour { get; set; } = new() { V4 = new(1f, 1f, 0f, 1f) };

    [UserSetting("点位圈默认颜色")]
    public ScriptColor PointCircleColour { get; set; } = new() { V4 = new(0f, 0.8f, 1f, 0.6f) };
    [UserSetting("始终显示猫爪法的三个点位圈")]
    public bool ShowPointCircles { get; set; } = true;
    [UserSetting("提示穿入方向\n即 MMW 攻略里穿猫爪的左／右，以面向当前轮次地火（或猫爪三角形的中心）时的左右为准，并非面向 Boss 的左右。\n建议仅在开启“始终显示猫爪法的三个点位圈”时启用。")]
    public bool ShowEntryDirection { get; set; } = true;

    private int fireRound;
    private bool collectingBlades;
    private readonly List<Blade> blades = [];
    private readonly object drawLock = new();
    private DateTime castEventTime;
    private long castStartedAt;
    private TaskCompletionSource<CatPawRoute> routeReady = new();
    private readonly HashSet<string> handledMethods = [];

    private record CatPawRoute(Vector2[] Points, string Side, bool IsInverse);

    private record Blade(uint Id, double X, double Y, double Rotation);

    private void ResetStates()
    {
        // 更换地火轮次，停止上一轮尚未显示的横幅。
        fireRound++;
        collectingBlades = false;
        blades.Clear();
        castEventTime = default;
        routeReady.TrySetCanceled();
        routeReady = new();
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
            BeginFireRound(@event, accessory, nameof(FulgentBladeStart));
    }

    private bool BeginFireRound(Event @event, ScriptAccessory accessory, string method)
    {
        // 同一次读条的几个方法共用一个轮次，不依赖方法执行顺序。
        if (castEventTime != @event.DateTime)
        {
            ResetStates();
            accessory.Method.RemoveDraw("^FRUPatch_CatPaw_");
            castEventTime = @event.DateTime;
            castStartedAt = Environment.TickCount64;
            collectingBlades = true;
        }
        return handledMethods.Add(method);
    }

    [ScriptMethod(name: "P5地火数据捕获", eventType: EventTypeEnum.ObjectChanged,
        eventCondition: ["Operate:Add", "DataId:2014199"], userControl: false)]
    public void CaptureBlades(Event @event, ScriptAccessory accessory)
    {
        var position = JsonConvert.DeserializeObject<Vector3>(@event["SourcePosition"]);
        var blade = new Blade(Convert.ToUInt32(@event["SourceId"], 16),
            position.X, position.Z, Convert.ToDouble(@event["SourceRotation"]));
        lock (drawLock)
        {
            // 实体通常先于读条生成，延迟一秒后再存入读条初始化的轮次。
            int round = collectingBlades ? fireRound : fireRound + 1;
            _ = CaptureBladeAfterDelay(blade, round);
        }
    }

    private async Task CaptureBladeAfterDelay(Blade blade, int round)
    {
        await Task.Delay(1000);
        lock (drawLock)
        {
            if (round != fireRound || !collectingBlades || blades.Any(item => item.Id == blade.Id)) return;
            blades.Add(blade);
            if (blades.Count == 6) ProcessBlades();
        }
    }

    private void ProcessBlades()
    {
        // 按实体 ID 从大到小排序，前两条线确定首组交点。
        var sorted = blades.OrderByDescending(blade => blade.Id).ToArray();
        var point1 = GetIntersection(sorted[0], sorted[1]);
        var point2 = GetIntersection(sorted[2], sorted[3]);
        var point3 = GetIntersection(sorted[4], sorted[5]);
        var middlePoint = (point1 + point3) / 2;
        collectingBlades = false;
        routeReady.TrySetResult(CalculateCatPawRoute(point1, point2, middlePoint));
    }

    [ScriptMethod(name: "光尘之剑（地火）猫爪法指路（MMW）", eventType: EventTypeEnum.StartCasting,
        eventCondition: ["ActionId:40306"])]
    public void CatPawGuidance(Event @event, ScriptAccessory accessory)
    {
        lock (drawLock)
        {
            if (!BeginFireRound(@event, accessory, nameof(CatPawGuidance))) return;
            _ = DrawGuidance(accessory, fireRound, castStartedAt, routeReady.Task, (int)(GuidanceDelaySeconds * 1000));
        }
    }

    private async Task DrawGuidance(ScriptAccessory accessory, int round, long startedAt, Task<CatPawRoute> ready, int showAt)
    {
        var data = await WaitForRoute(ready, startedAt, showAt);
        if (data is null) return;
        lock (drawLock)
        {
            if (round != fireRound) return;
            var route = data.Points;
            int elapsed = (int)(Environment.TickCount64 - startedAt);
            if (elapsed >= 29000) return;

            // 首次显示时间可调；B、C、回 A 和结束分别在读条开始后 19、23、27、29 秒。
            int[] times = [0, 19000, 23000, 27000, 29000];
            if (ShowPointCircles)
            {
                // 三个点位圈持续显示到最后一次回 A 的指路结束。
                for (int point = 0; point < 3; point++)
                    DrawPointCircle(accessory, $"Point_{(char)('A' + point)}", route[point],
                        1.00f, 0.90f, PointCircleColour.V4, 0, 29000 - elapsed);
            }
            for (int step = 0; step < route.Length; step++)
            {
                int delay = Math.Max(0, times[step] - elapsed);
                int duration = times[step + 1] - Math.Max(elapsed, times[step]);
                if (duration <= 0) continue;
                DrawPointCircle(accessory, $"ActivePoint_{step}", route[step],
                    1.05f, 0.95f, CurrentStepColour.V4, delay, duration);

                // 当前点箭头从玩家指向当前点，预览箭头固定从当前点指向下一点。
                DrawArrow(accessory, $"Current_{step}", null, route[step],
                    CurrentStepColour.V4, delay, duration);

                if (step == route.Length - 1) continue;
                DrawPointCircle(accessory, $"NextPoint_{step}", route[step + 1],
                    1.05f, 0.95f, NextStepColour.V4, delay, duration);

                DrawArrow(accessory, $"Next_{step}", route[step], route[step + 1],
                    NextStepColour.V4, delay, duration);
            }
        }
    }

    [ScriptMethod(name: "光尘之剑（地火）起点横幅提示", eventType: EventTypeEnum.StartCasting,
        eventCondition: ["ActionId:40306"])]
    public void CatPawStartBanner(Event @event, ScriptAccessory accessory)
    {
        lock (drawLock)
        {
            if (!BeginFireRound(@event, accessory, nameof(CatPawStartBanner))) return;
            _ = ShowStartBanner(accessory, fireRound, castStartedAt, routeReady.Task);
        }
    }

    private async Task ShowStartBanner(ScriptAccessory accessory, int round, long startedAt, Task<CatPawRoute> ready)
    {
        var data = await WaitForRoute(ready, startedAt, 10000);
        if (data is null) return;
        lock (drawLock)
        {
            if (round != fireRound || Environment.TickCount64 - startedAt >= 19000) return;
            string entry = ShowEntryDirection ? EntryDirection(data.IsInverse) : "按箭头穿入";
            accessory.Method.TextInfo($"场地{data.Side}侧开始，稍后{entry}", 5000);
        }
    }

    [ScriptMethod(name: "光尘之剑（地火）后续点横幅提示", eventType: EventTypeEnum.StartCasting,
        eventCondition: ["ActionId:40306"])]
    public void CatPawMoveBanner(Event @event, ScriptAccessory accessory)
    {
        lock (drawLock)
        {
            if (!BeginFireRound(@event, accessory, nameof(CatPawMoveBanner))) return;
            _ = ShowMoveBanners(accessory, fireRound, castStartedAt, routeReady.Task);
        }
    }

    private static async Task<CatPawRoute?> WaitForRoute(Task<CatPawRoute> ready, long startedAt, int showAt)
    {
        CatPawRoute data;
        try { data = await ready; }
        catch (TaskCanceledException) { return null; }
        int delay = (int)(showAt - (Environment.TickCount64 - startedAt));
        if (delay > 0) await Task.Delay(delay);
        return data;
    }

    private static CatPawRoute CalculateCatPawRoute(Vector2 point1, Vector2 point2, Vector2 middlePoint)
    {
        // 根据交点中点的位置，确定本轮在场地哪一侧处理地火。
        Vector2[] centres = [new(100.000f, 92.929f), new(107.071f, 100.000f),
            new(100.000f, 107.071f), new(92.929f, 100.000f)];
        int centreIndex = Enumerable.Range(0, centres.Length)
            .OrderBy(index => Vector2.DistanceSquared(centres[index], middlePoint)).First();
        Vector2 centre = centres[centreIndex];

        // 从实体顺序确定首组和第二组，提前计算相对处理中心的八方向编号。
        Vector2 firstOffset = point1 - middlePoint;
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
        return new CatPawRoute([pointA, pointB, pointC, pointA], sides[centreIndex], isInverse);
    }

    private static string EntryDirection(bool isInverse) => isInverse ? "向右逆时针穿入" : "向左顺时针穿入";

    private async Task ShowMoveBanners(ScriptAccessory accessory, int round, long startedAt, Task<CatPawRoute> ready)
    {
        foreach (int time in new[] { 19000, 23000, 27000 })
        {
            var data = await WaitForRoute(ready, startedAt, time);
            if (data is null) return;
            lock (drawLock)
            {
                if (round != fireRound) return;
                accessory.Method.TextInfo(ShowEntryDirection ? EntryDirection(data.IsInverse) : "穿入下一点", 2000);
            }
        }
    }

    private static void DrawArrow(ScriptAccessory accessory, string name, Vector2? start, Vector2 target,
        Vector4 colour, int delay, int duration)
    {
        var arrow = accessory.Data.GetDefaultDrawProperties();
        arrow.Name = $"FRUPatch_CatPaw_{name}";
        if (start is Vector2 position)
            arrow.Position = new(position.X, 0, position.Y);
        else
            arrow.Owner = accessory.Data.Me;
        arrow.TargetPosition = new(target.X, 0, target.Y);
        arrow.Scale = new(2);
        arrow.ScaleMode |= ScaleMode.YByDistance;
        arrow.Color = colour;
        arrow.Delay = delay;
        arrow.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, arrow);
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
