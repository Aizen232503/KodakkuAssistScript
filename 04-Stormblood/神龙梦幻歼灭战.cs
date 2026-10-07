// OnlineRepo: false
using System;
using System.Linq;
using System.Numerics;
using System.Runtime.Intrinsics.Arm;
using Dalamud.Utility.Numerics;
using KodakkuAssist.Extensions;
using KodakkuAssist.Module.Draw;
using KodakkuAssist.Module.Draw.Manager;
using KodakkuAssist.Module.GameEvent;
using KodakkuAssist.Module.GameOperate;
using KodakkuAssist.Script;
using Newtonsoft.Json;

namespace VanivilleKalosScript;

[ScriptType(
    guid: "4F77CA60-2606-B42B-376E-1544C1DF1274",
    name: "神龙梦幻歼灭战",
    territorys: [],
    version: "0.0.0.1",
    author: "Aizen232503 卡璞·仙仙"
)]
public class Shinryu
{
    [UserSetting("测试")]
    public bool testMode { get; set; } = false;

    // 初始化地板状态
    private readonly int[] floorStateCounts = new int[9];

    private void ResetFloorStates()
    {
        for (int i = 0; i < floorStateCounts.Length; i++)
            floorStateCounts[i] = 2;
    }

    public void Init(ScriptAccessory accessory)
    {
        ResetFloorStates();
        accessory.Log.Debug($"地板状态已初始化: {string.Join(',', floorStateCounts)}");
    }

    [ScriptMethod(
        name: "冰柱突刺",
        eventType: EventTypeEnum.StartCasting,
        eventCondition: ["ActionId:9712"]
    )]
    public void DrawCircle(Event @event, ScriptAccessory accessory)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = "冰柱突刺";
        dp.Owner = Convert.ToUInt32(@event["SourceId"], 16);
        dp.Scale = new(10, 62);
        dp.DestoryAt = 3700;
        dp.Color = accessory.Data.DefaultDangerColor;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Rect, dp);
    }

    [ScriptMethod(
        name: "巨浪",
        eventType: EventTypeEnum.StartCasting,
        eventCondition: ["ActionId:9690"]
    )]
    public void BigWave(Event @event, ScriptAccessory accessory)
    {
        if (accessory.Data.MyObject?.EntityId is not uint myId)
            return;
        accessory.Log.Debug($"巨浪,{@event.TargetPosition},{@event.TargetRotation}");
        var dp = DrawHelper.DrawStraightKnockBack(
            accessory,
            myId,
            @event.TargetRotation,
            35,
            0,
            9700,
            "巨浪",
            3,
            true
        );
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Displacement, dp);
    }

    public static int GetSquareIndex(Vector3 pos)
    {
        // 东北角中心点
        float startX = 20f;
        float startZ = -20f;
        float size = 20f;

        // 行：Z轴往南为正，行号从0到2
        int row = (int)Math.Floor((pos.Z - startZ + size / 2) / size);
        // 列：X轴往西为正，列号从0到2
        int col = (int)Math.Floor((startX - pos.X + size / 2) / size);

        // 检查是否越界
        if (col < 0 || col > 2 || row < 0 || row > 2)
            return -1; // 不在场地内

        // 计算编号（东北角为1，左到右，上到下）
        return col * 3 + row;
    }

    [ScriptMethod(
        name: "地板状态计数",
        eventType: EventTypeEnum.ObjectEffect,
        eventCondition: [],
        userControl: false
    )]
    public void FloorEffect(Event @event, ScriptAccessory accessory)
    {
        accessory.Log.Debug($"SourceId,{@event.SourceId}");
        if (!int.TryParse(@event["Id2"]?.ToString(), out var id2))
            return;

        if (id2 > 8192)
            return;
        var obj = accessory.Data.Objects.FirstOrDefault(o => o.EntityId == @event.SourceId);
        if (obj == null || obj.DataId != 2007457)
        {
            return;
        }
        var floorIndex = GetSquareIndex(@event.SourcePosition);
        var newState = id2 switch
        {
            8192 => 0, // 破坏
            4096 => 0, // 破坏
            8 => 1, // 裂纹
            _ => -1,
        };
        floorStateCounts[floorIndex] = newState;
        accessory.Log.Debug($"地板状态更新: {string.Join(',', floorStateCounts)}");
    }

    [ScriptMethod(
        name: "尾部猛击",
        eventType: EventTypeEnum.StartCasting,
        eventCondition: ["ActionId:regex:^(9698|9741)$"]
    )]
    public void TailAttack(Event @event, ScriptAccessory accessory)
    {
        accessory.Log.Debug($"尾部方向: {(@event.EffectPosition.Z > 0 ? "右" : "左")}");
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = "尾部猛击";
        dp.Position = @event.EffectPosition;
        dp.Rotation = @event.SourceRotation;
        dp.Scale = new(20, 40);
        dp.DestoryAt = 2700;
        dp.Color = accessory.Data.DefaultDangerColor;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Rect, dp);
        accessory.Log.Debug($"{dp}");
    }

    [ScriptMethod(
        name: "闪电",
        eventType: EventTypeEnum.StartCasting,
        eventCondition: ["ActionId:regex:^(9706)$"]
    )]
    public void Thunder(Event @event, ScriptAccessory accessory)
    {
        var puddles = accessory.Data.Objects.Where(o => o.DataId == 2004237).ToList();

        foreach (var p in puddles)
        {
            accessory.Log.Debug($"SHUIQUAN: {p.DataId}");
            var dp = accessory.Data.GetDefaultDrawProperties();
            dp.Name = "闪电（水圈）";
            dp.Owner = p.GameObjectId;
            dp.Scale = new(5);
            dp.DestoryAt = 7700;
            dp.Color = new Vector4(1f, 0f, 0f, 1f);
            accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Circle, dp);
        }
    }

    [ScriptMethod(
        name: "测试",
        eventType: EventTypeEnum.StartCasting,
        eventCondition: ["ActionId:24284"]
    )]
    public void Test(Event @event, ScriptAccessory accessory)
    {
        accessory.Log.Debug(
            $"位置,{@event.TargetPosition},位于方块{GetSquareIndex(@event.TargetPosition)}"
        );
    }
}

public static class DrawHelper
{
    public static DrawPropertiesEdit DrawStraightKnockBack(
        this ScriptAccessory accessory,
        uint ownerId,
        float rotation,
        float length,
        int delay,
        int destroy,
        string name,
        float width = 1.5f,
        bool byTime = false
    )
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Scale = new Vector2(width, length);
        dp.Owner = ownerId;
        dp.FixRotation = true;
        dp.Rotation = rotation;
        dp.Color = accessory.Data.DefaultDangerColor;
        dp.Delay = delay;
        dp.DestoryAt = destroy;
        dp.ScaleMode |= byTime ? ScaleMode.ByTime : ScaleMode.None;

        accessory.Log.Debug($"{dp}");
        return dp;
    }

    public static DrawPropertiesEdit DrawStraightKnockBack(
        this ScriptAccessory accessory,
        ulong ownerId,
        float rotation,
        float length,
        int delay,
        int destroy,
        string name,
        float width = 1.5f,
        bool byTime = false
    )
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Scale = new Vector2(width, length);
        dp.Owner = ownerId;
        dp.FixRotation = true;
        dp.Rotation = rotation;
        dp.Color = accessory.Data.DefaultDangerColor;
        dp.Delay = delay;
        dp.DestoryAt = destroy;
        dp.ScaleMode |= byTime ? ScaleMode.ByTime : ScaleMode.None;
        return dp;
    }

    public static DrawPropertiesEdit DrawKnockBack(
        this ScriptAccessory accessory,
        ulong ownerId,
        object target,
        float length,
        int delay,
        int destroy,
        string name,
        float width = 1.5f,
        bool byTime = false
    )
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Scale = new Vector2(width, length);
        dp.Owner = ownerId;

        if (target is uint or ulong)
        {
            dp.TargetObject = (ulong)target;
        }
        else if (target is Vector3 tpos)
        {
            dp.TargetPosition = tpos;
        }
        else
        {
            throw new ArgumentException("DrawKnockBack的目标类型输入错误");
        }

        dp.Rotation = float.Pi;
        dp.Color = accessory.Data.DefaultDangerColor;
        dp.Delay = delay;
        dp.DestoryAt = destroy;
        dp.ScaleMode |= byTime ? ScaleMode.ByTime : ScaleMode.None;
        return dp;
    }
}
