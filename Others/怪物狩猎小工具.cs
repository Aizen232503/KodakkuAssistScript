using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Intrinsics.Arm;
using System.Xml.Linq;
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
    guid: "8C2D2169-6976-A826-CC1E-41333D1C3FDC",
    name: "怪物狩猎小工具",
    territorys: [
        // 2.0 拉诺西亚
        134,
        135,
        137,
        138,
        139,
        180,
        // 2.0 萨纳兰
        140,
        141,
        145,
        146,
        147,
        // 2.0 黑衣森林
        148,
        152,
        153,
        154,
        // 2.0
        155,
        156,
        // 3.0
        397,
        398,
        399,
        400,
        401,
        402,
        // 4.0
        612,
        613,
        614,
        620,
        621,
        622,
        // 5.0
        813,
        814,
        815,
        816,
        817,
        818,
        // 6.0
        956,
        957,
        958,
        959,
        960,
        961,
        // 7.0
        1187,
        1188,
        1189,
        1190,
        1191,
        1192,
        // ...
    ],
    version: "0.0.0.3",
    author: "Aizen232503 卡璞·仙仙"
)]
public class HuntAssistant
{
    private static int repeatPullInterval = 20;

    [UserSetting("拉脱后重新报开怪的最小时间（秒，非负数；0 为不限制重复播报）")]
    public static int minRepeatPullInterval
    {
        get => repeatPullInterval;
        set => repeatPullInterval = Math.Max(0, value);
    }

    public enum RankOptionsEnum
    {
        全部_S和A怪,
        仅S怪,
        仅A怪,
        关闭,
    }

    [UserSetting("----- 功能总开关（此行仅作分隔，无实际意义） -----")]
    public static bool dividerLine1 { get; set; } = false;

    [UserSetting("是否使用恶名精英出现TTS")]
    public static bool enableSpawnTTS { get; set; } = true;

    [UserSetting("是否使用恶名精英出现横幅")]
    public static bool enableSpawnBanner { get; set; } = true;

    [UserSetting("是否使用恶名精英开怪TTS")]
    public static bool enablePullTTS { get; set; } = true;

    [UserSetting("是否使用恶名精英开怪横幅")]
    public static bool enablePullBanner { get; set; } = false;

    [UserSetting("是否使用恶名精英拉脱TTS")]
    public static bool enableOutOfCombatTTS { get; set; } = false;

    [UserSetting("是否使用恶名精英拉脱横幅")]
    public static bool enableOutOfCombatBanner { get; set; } = true;

    [UserSetting("----- 恶名精英出现提示开关（此行仅作分隔，无实际意义） -----")]
    public static bool dividerLine2 { get; set; } = false;

    [UserSetting("2.0恶名精英出现提示")]
    public static RankOptionsEnum SpawnOption2 { get; set; } = RankOptionsEnum.仅S怪;

    [UserSetting("3.0恶名精英出现提示")]
    public static RankOptionsEnum SpawnOption3 { get; set; } = RankOptionsEnum.仅S怪;

    [UserSetting("4.0恶名精英出现提示")]
    public static RankOptionsEnum SpawnOption4 { get; set; } = RankOptionsEnum.仅S怪;

    [UserSetting("5.0恶名精英出现提示")]
    public static RankOptionsEnum SpawnOption5 { get; set; } = RankOptionsEnum.仅S怪;

    [UserSetting("6.0恶名精英出现提示")]
    public static RankOptionsEnum SpawnOption6 { get; set; } = RankOptionsEnum.仅S怪;

    [UserSetting("7.0恶名精英出现提示")]
    public static RankOptionsEnum SpawnOption7 { get; set; } = RankOptionsEnum.仅S怪;

    [UserSetting("----- 恶名精英开怪和拉脱提示开关（此行仅作分隔，无实际意义） -----")]
    public static bool dividerLine3 { get; set; } = false;

    [UserSetting("2.0恶名精英开怪和拉脱提示")]
    public static RankOptionsEnum PullOption2 { get; set; } = RankOptionsEnum.关闭;

    [UserSetting("3.0恶名精英开怪和拉脱提示")]
    public static RankOptionsEnum PullOption3 { get; set; } = RankOptionsEnum.关闭;

    [UserSetting("4.0恶名精英开怪和拉脱提示")]
    public static RankOptionsEnum PullOption4 { get; set; } = RankOptionsEnum.关闭;

    [UserSetting("5.0恶名精英开怪和拉脱提示")]
    public static RankOptionsEnum PullOption5 { get; set; } = RankOptionsEnum.关闭;

    [UserSetting("6.0恶名精英开怪和拉脱提示")]
    public static RankOptionsEnum PullOption6 { get; set; } = RankOptionsEnum.关闭;

    [UserSetting("7.0恶名精英开怪和拉脱提示")]
    public static RankOptionsEnum PullOption7 { get; set; } = RankOptionsEnum.仅S怪;

    private static readonly object StateLock = new();

    public readonly record struct SceneKey(uint WorldId, uint TerritoryId, uint InstanceId);
    public readonly record struct MonsterKey(SceneKey Scene, ulong EntityId);
    private readonly record struct ObservedMonster(ulong EntityId, int DataId, bool IsPulled);

    private static SceneKey activeScene;
    private static bool sceneReady;
    private static long sceneGeneration;
    private static bool sceneScanRunning;
    private static DateTime sceneReadyAtUtc;

    public class MonsterState
    {
        public ulong EntityId { get; set; }
        public int DataId { get; set; }
        public bool IsPulled { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime? LastOutTime { get; set; }
        public DateTime? RemovedAt { get; set; }
        public bool SpawnObserved { get; set; }
    }

    // 当前服务器、地图、分线和对象共同标识怪物；每次进入场景重新建立记录。
    public static readonly ConcurrentDictionary<MonsterKey, MonsterState> MonsterStates = new();

    public void Init(ScriptAccessory accessory)
    {
        var valid = TryReadScene(accessory, out var scene, out var loading);
        lock (StateLock)
        {
            // 框架在战斗重置时也会调用 Init；同一场景不重新播报。
            if (sceneReady && valid && !loading && scene == activeScene)
                return;
            InvalidateScene();
            StartSceneScan(accessory);
        }
        accessory.Log.Debug($"怪物狩猎小工具重载，等待当前场景加载");
    }

    // 使用公开的客户端结构，不访问可达鸭内部的 Service 类。
    private static unsafe bool TryReadScene(ScriptAccessory accessory, out SceneKey scene, out bool loading)
    {
        scene = default;
        loading = true;
        var conditions = FFXIVClientStructs.FFXIV.Client.Game.Conditions.Instance();
        var game = FFXIVClientStructs.FFXIV.Client.Game.GameMain.Instance();
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        if (conditions == null || game == null || ui == null)
            return false;

        loading = conditions->BetweenAreas || conditions->BetweenAreas51;
        var player = accessory.Data.MyObject;
        if (player == null)
            return false;

        scene = new SceneKey(
            player.CurrentWorld.RowId,
            game->CurrentTerritoryTypeId,
            ui->PublicInstance.InstanceId
        );
        // 无分线地图的 InstanceId 可以为 0，不能将它作为无效场景。
        return scene.WorldId != 0 && scene.TerritoryId != 0;
    }

    // 下列状态管理方法只能在 StateLock 内调用。
    private static void InvalidateScene()
    {
        sceneGeneration++;
        sceneReady = false;
        sceneScanRunning = false;
        MonsterStates.Clear();
    }

    private static void StartSceneScan(ScriptAccessory accessory)
    {
        if (sceneScanRunning)
            return;
        sceneScanRunning = true;
        var generation = sceneGeneration;
        // 有界等待，无永久计时器或游戏事件订阅；Init 会使旧任务失效。
        _ = System.Threading.Tasks.Task.Run(() => ScanSceneAsync(accessory, generation));
    }

    private static async System.Threading.Tasks.Task ScanSceneAsync(ScriptAccessory accessory, long generation)
    {
        SceneKey? candidate = null;
        long stableSince = 0;
        var deadline = Environment.TickCount64 + 20000;
        try
        {
            while (Environment.TickCount64 < deadline)
            {
                lock (StateLock)
                {
                    if (generation != sceneGeneration)
                        return;
                }

                if (!TryReadScene(accessory, out var scene, out var loading) || loading)
                {
                    candidate = null;
                }
                else if (candidate != scene)
                {
                    candidate = scene;
                    stableSince = Environment.TickCount64;
                }
                else if (Environment.TickCount64 - stableSince >= 400)
                {
                    var observed = new List<ObservedMonster>();
                    foreach (var obj in accessory.Data.Objects)
                    {
                        var dataId = (int)obj.DataId;
                        if (!NMDict.ContainsKey(dataId) || obj.IsDead)
                            continue;
                        if (obj is not KodakkuAssist.Data.ICharacter character || character.CurrentHp == 0)
                            continue;
                        observed.Add(new ObservedMonster(
                            obj.GameObjectId,
                            dataId,
                            (character.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) != 0
                        ));
                    }

                    // 扫描前后必须属于同一场景，防止混入切线期间的对象表。
                    if (!TryReadScene(accessory, out var afterScan, out loading) || loading || afterScan != scene)
                    {
                        candidate = null;
                        continue;
                    }

                    var announcements = new List<ObservedMonster>();
                    lock (StateLock)
                    {
                        if (generation != sceneGeneration)
                            return;
                        MonsterStates.Clear();
                        activeScene = scene;
                        sceneReadyAtUtc = DateTime.UtcNow;
                        foreach (var monster in observed)
                        {
                            if (MonsterStates.TryAdd(
                                new MonsterKey(scene, monster.EntityId),
                                new MonsterState
                                {
                                    EntityId = monster.EntityId,
                                    DataId = monster.DataId,
                                    Name = NMDict[monster.DataId].Name,
                                    IsPulled = monster.IsPulled,
                                    SpawnObserved = true,
                                }
                            ))
                                announcements.Add(monster);
                        }
                        sceneReady = true;
                    }

                    accessory.Log.Debug($"当前场景：服务器 {scene.WorldId}，地图 {scene.TerritoryId}，分线 {scene.InstanceId}");
                    foreach (var monster in announcements)
                        NotifySpawn(monster.EntityId, monster.DataId, scene, generation, accessory);
                    return;
                }

                await System.Threading.Tasks.Task.Delay(200).ConfigureAwait(false);
            }
            accessory.Log.Debug("等待场景加载超时，将在下一次游戏事件时重试");
        }
        catch (Exception ex)
        {
            accessory.Log.Debug($"场景读取或扫描失败，将在下一次游戏事件时重试：{ex.Message}");
        }
        finally
        {
            lock (StateLock)
            {
                if (generation == sceneGeneration)
                    sceneScanRunning = false;
            }
        }
    }

    private static bool TryGetEventScene(Event @event, ScriptAccessory accessory, out SceneKey scene, out long generation)
    {
        var valid = TryReadScene(accessory, out scene, out var loading);
        lock (StateLock)
        {
            if (sceneReady && (!valid || loading || scene != activeScene))
                InvalidateScene();
            generation = sceneGeneration;
            if (!sceneReady)
            {
                StartSceneScan(accessory);
                return false;
            }

            // 旧线或加载期间产生的异步事件，不得影响重新建立的当前线状态。
            return valid && !loading && scene == activeScene
                && @event.DateTime.ToUniversalTime() >= sceneReadyAtUtc;
        }
    }

    private static bool MatchesActiveScene(SceneKey scene, long generation)
        => sceneReady && activeScene == scene && sceneGeneration == generation;

    private static bool CanNotify(SceneKey scene, long generation, ScriptAccessory accessory)
    {
        if (!TryReadScene(accessory, out var current, out var loading) || loading || current != scene)
            return false;
        lock (StateLock)
            return MatchesActiveScene(scene, generation);
    }

    private static bool TryParseHexId(string? raw, out ulong id)
    {
        id = 0;
        if (string.IsNullOrEmpty(raw))
            return false;
        var s = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw[2..] : raw;
        return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out id);
    }

    private static string ParseRank(int rank) => rank switch
    {
        0 => "SSMinion",
        1 => "B",
        2 => "A",
        3 => "S",
        4 => "SS",
        _ => "",
    };

    private static void CleanupRemovedStates(DateTime now)
    {
        var retention = TimeSpan.FromSeconds(Math.Max(60, minRepeatPullInterval));
        foreach (var entry in MonsterStates)
            if (entry.Value.RemovedAt is DateTime removedAt && now - removedAt >= retention)
                MonsterStates.TryRemove(entry.Key, out _);
    }

    private static bool CheckSpawnVersionRestrict(int dataId, bool usePullOption = false)
    {
        if (!NMDict.ContainsKey(dataId) || NMDict[dataId].Rank <= 1)
            return false;
        var version = NMDict[dataId].Version;
        var rank = NMDict[dataId].Rank;
        var option = version switch
        {
            2 => usePullOption ? PullOption2 : SpawnOption2,
            3 => usePullOption ? PullOption3 : SpawnOption3,
            4 => usePullOption ? PullOption4 : SpawnOption4,
            5 => usePullOption ? PullOption5 : SpawnOption5,
            6 => usePullOption ? PullOption6 : SpawnOption6,
            7 => usePullOption ? PullOption7 : SpawnOption7,
            _ => RankOptionsEnum.关闭,
        };
        return option switch
        {
            RankOptionsEnum.全部_S和A怪 => rank >= 2,
            RankOptionsEnum.仅S怪 => rank >= 3,
            RankOptionsEnum.仅A怪 => rank == 2,
            _ => false,
        };
    }

    private static void NotifySpawn(ulong entityId, int dataId, SceneKey scene, long generation, ScriptAccessory accessory)
    {
        if (!CanNotify(scene, generation, accessory))
            return;
        accessory.Log.Debug($"发现{entityId} {dataId} {ParseRank(NMDict[dataId].Rank)} {NMDict[dataId].Name}");
        if (!CheckSpawnVersionRestrict(dataId))
            return;
        if (enableSpawnTTS)
            accessory.Method.EdgeTTS($"{ParseRank(NMDict[dataId].Rank)} {NMDict[dataId].Name}");
        if (enableSpawnBanner)
            accessory.Method.TextInfo($"发现{ParseRank(NMDict[dataId].Rank)}怪：{NMDict[dataId].Name}", 5000);
    }

    [ScriptMethod(name: "场景切换检查", eventType: EventTypeEnum.Territory, eventCondition: [], userControl: false)]
    public void SceneHelper(Event @event, ScriptAccessory accessory)
        => TryGetEventScene(@event, accessory, out _, out _);

    [ScriptMethod(name: "出现提醒", eventType: EventTypeEnum.AddCombatant, eventCondition: [], userControl: false)]
    public void SpawnHelper(Event @event, ScriptAccessory accessory)
    {
        // 所有 AddCombatant 都检查场景，即使当前条目不是狩猎怪。
        if (!TryGetEventScene(@event, accessory, out var scene, out var generation))
            return;
        if (!int.TryParse(@event["DataId"], out var dataId)
            || !TryParseHexId(@event["SourceId"], out var entityId) || !NMDict.ContainsKey(dataId))
            return;
        var obj = accessory.Data.Objects.SearchById(entityId);
        if (obj == null || obj.DataId != (uint)dataId || obj.IsDead)
            return;

        lock (StateLock)
        {
            if (!MatchesActiveScene(scene, generation))
                return;
            CleanupRemovedStates(DateTime.UtcNow);
            var key = new MonsterKey(scene, entityId);
            if (MonsterStates.TryGetValue(key, out var existing) && existing.DataId == dataId)
            {
                existing.RemovedAt = null;
                if (existing.SpawnObserved)
                    return;
                existing.SpawnObserved = true;
            }
            else
            {
                MonsterStates[key] = new MonsterState
                {
                    EntityId = entityId, DataId = dataId, Name = NMDict[dataId].Name, SpawnObserved = true,
                };
            }
        }
        NotifySpawn(entityId, dataId, scene, generation, accessory);
    }

    [ScriptMethod(name: "开怪提醒", eventType: EventTypeEnum.ActionEffect,
        eventCondition: ["ActionId:regex:^(7|8)$"], userControl: false)]
    public void PullHelper(Event @event, ScriptAccessory accessory)
    {
        if (!TryGetEventScene(@event, accessory, out var scene, out var generation))
            return;
        if (!int.TryParse(@event["TargetDataId"], out var dataId)
            || !TryParseHexId(@event["TargetId"], out var entityId) || !NMDict.ContainsKey(dataId))
            return;
        var obj = accessory.Data.Objects.SearchById(entityId);
        if (obj == null || obj.DataId != (uint)dataId || obj.IsDead)
            return;

        bool suppressNotification;
        lock (StateLock)
        {
            if (!MatchesActiveScene(scene, generation))
                return;
            var now = DateTime.UtcNow;
            CleanupRemovedStates(now);
            var key = new MonsterKey(scene, entityId);
            if (!MonsterStates.TryGetValue(key, out var state) || state.DataId != dataId)
            {
                state = new MonsterState { EntityId = entityId, DataId = dataId, Name = NMDict[dataId].Name };
                MonsterStates[key] = state;
            }
            state.RemovedAt = null;
            if (state.IsPulled)
                return;
            suppressNotification = state.LastOutTime.HasValue
                && now - state.LastOutTime.Value < TimeSpan.FromSeconds(minRepeatPullInterval);
            state.IsPulled = true;
        }

        if (suppressNotification || !CanNotify(scene, generation, accessory))
            return;
        accessory.Log.Debug($"{entityId} {dataId}{NMDict[dataId].Name} 已开怪");
        if (!CheckSpawnVersionRestrict(dataId, true))
            return;
        if (enablePullTTS)
            accessory.Method.EdgeTTS($"{NMDict[dataId].Name} 已开怪");
        if (enablePullBanner)
            accessory.Method.TextInfo($"{NMDict[dataId].Name} 已开怪", 3000);
    }

    [ScriptMethod(name: "拉脱提醒", eventType: EventTypeEnum.UpdateHpMp, eventCondition: [], userControl: false)]
    public void OutOfCombatHelper(Event @event, ScriptAccessory accessory)
    {
        if (!TryGetEventScene(@event, accessory, out var scene, out var generation)
            || !TryParseHexId(@event["SourceId"], out var entityId))
            return;
        int dataId;
        string name;
        lock (StateLock)
        {
            // 沿用原有 UpdateHpMp 拉脱判定，只增加场景归属检查。
            if (!MatchesActiveScene(scene, generation)
                || !MonsterStates.TryGetValue(new MonsterKey(scene, entityId), out var state) || !state.IsPulled)
                return;
            state.IsPulled = false;
            state.LastOutTime = DateTime.UtcNow;
            dataId = state.DataId;
            name = state.Name;
        }
        if (!CanNotify(scene, generation, accessory))
            return;
        accessory.Log.Debug($"{entityId} {dataId}{name} 已拉脱");
        if (!CheckSpawnVersionRestrict(dataId, true))
            return;
        if (enableOutOfCombatTTS)
            accessory.Method.EdgeTTS($"{name}已拉脱");
        if (enableOutOfCombatBanner)
            accessory.Method.TextInfo($"{name}已拉脱", 3000, true);
    }

    [ScriptMethod(name: "怪物离开范围记录", eventType: EventTypeEnum.RemoveCombatant, eventCondition: [], userControl: false)]
    public void RemoveHelper(Event @event, ScriptAccessory accessory)
    {
        if (!TryGetEventScene(@event, accessory, out var scene, out var generation)
            || !TryParseHexId(@event["SourceId"], out var entityId))
            return;
        lock (StateLock)
        {
            if (!MatchesActiveScene(scene, generation))
                return;
            var now = DateTime.UtcNow;
            CleanupRemovedStates(now);
            if (MonsterStates.TryGetValue(new MonsterKey(scene, entityId), out var state))
                state.RemovedAt ??= now;
        }
    }

    [ScriptMethod(name: "怪物死亡清理", eventType: EventTypeEnum.Death, eventCondition: [], userControl: false)]
    public void DeathHelper(Event @event, ScriptAccessory accessory)
    {
        if (!TryGetEventScene(@event, accessory, out var scene, out var generation)
            || !TryParseHexId(@event["TargetId"], out var entityId))
            return;
        lock (StateLock)
            if (MatchesActiveScene(scene, generation))
                MonsterStates.TryRemove(new MonsterKey(scene, entityId), out _);
    }


    //恶名精英字典
    public static readonly Dictionary<int, (string Name, int Rank, int Version)> NMDict = new()
    {
        [3176] = ("击刺魔蜂索菲", 1, 2),
        [3177] = ("君王鬼蜻蜓", 1, 2),
        [3178] = ("天玑巨熊", 1, 2),
        [3179] = ("阴沟毒液", 1, 2),
        [3180] = ("奥弗杰恩", 1, 2),
        [3181] = ("加特林针鼹", 1, 2),
        [3182] = ("死灰复燃的阿尔宾", 1, 2),
        [3183] = ("永恒不灭的菲兰德副耀士", 1, 2),
        [3184] = ("花林女郎", 1, 2),
        [3185] = ("宽耳凶蝠", 1, 2),
        [3186] = ("血腥玛丽", 1, 2),
        [3187] = ("暗盔魔蟹", 1, 2),
        [3188] = ("米腊德罗斯蜂鸟", 1, 2),
        [3189] = ("巫刻猎鹫", 1, 2),
        [3190] = ("纳乌尔", 1, 2),
        [3191] = ("水蛭王", 1, 2),
        [3192] = ("弗内乌斯", 2, 2),
        [3193] = ("千眼凝胶", 2, 2),
        [3194] = ("盖得", 2, 2),
        [3195] = ("尾宿蛛蝎", 2, 2),
        [3196] = ("阿列刻特利昂", 2, 2),
        [3197] = ("花舞仙人刺", 2, 2),
        [3198] = ("玛赫斯", 2, 2),
        [3199] = ("札尼戈", 2, 2),
        [3200] = ("菲兰德的遗火", 2, 2),
        [3201] = ("丑男子 沃迦加", 2, 2),
        [3202] = ("乌克提希", 2, 2),
        [3203] = ("魔导地狱爪", 2, 2),
        [3204] = ("纳恩", 2, 2),
        [3205] = ("玛贝利", 2, 2),
        [3206] = ("角祖", 2, 2),
        [3207] = ("马拉克", 2, 2),
        [3208] = ("库雷亚", 2, 2),
        [3209] = ("雷德罗巨蛇", 3, 2),
        [3210] = ("乌尔伽鲁", 3, 2),
        [3211] = ("夺心魔", 3, 2),
        [3212] = ("千竿口花希达", 3, 2),
        [3213] = ("虚无探索者", 3, 2),
        [3214] = ("布隆特斯", 3, 2),
        [3215] = ("巴拉乌尔", 3, 2),
        [3216] = ("努纽努维", 3, 2),
        [3217] = ("蚓螈巨虫", 3, 2),
        [3218] = ("护土精灵", 3, 2),
        [3219] = ("咕尔呱洛斯", 3, 2),
        [3220] = ("伽洛克", 3, 2),
        [3221] = ("火愤牛", 3, 2),
        [3222] = ("南迪", 3, 2),
        [3223] = ("牛头黑神", 3, 2),
        [3224] = ("萨法特", 3, 2),
        [3225] = ("阿格里帕", 3, 2),
        [4527] = ("阿尔提克", 1, 3),
        [4528] = ("克鲁泽", 1, 3),
        [4529] = ("骨颌彗星兵", 1, 3),
        [4530] = ("提克斯塔", 1, 3),
        [4531] = ("翼肢鲎", 1, 3),
        [4532] = ("布拉巨猿", 1, 3),
        [4533] = ("斯奇塔利斯", 1, 3),
        [4534] = ("惊慌稻草龙", 1, 3),
        [4535] = ("斯奎克", 1, 3),
        [4536] = ("飞舞翼 萨努瓦力", 1, 3),
        [4537] = ("利西达斯", 1, 3),
        [4538] = ("全能机甲", 1, 3),
        [4539] = ("米勒卡", 2, 3),
        [4540] = ("卢芭", 2, 3),
        [4541] = ("派拉斯特暴龙", 2, 3),
        [4542] = ("双足飞龙之王", 2, 3),
        [4543] = ("机工兵 斯利普金克斯", 2, 3),
        [4544] = ("斯特拉斯", 2, 3),
        [4545] = ("布涅", 2, 3),
        [4546] = ("阿伽托斯", 2, 3),
        [4547] = ("恩克拉多斯", 2, 3),
        [4548] = ("西斯尤", 2, 3),
        [4549] = ("坎帕提", 2, 3),
        [4550] = ("恶臭狂花", 2, 3),
        [4551] = ("凯撒贝希摩斯", 3, 3),
        [4552] = ("神穆尔鸟", 3, 3),
        [4553] = ("苍白骑士", 3, 3),
        [4554] = ("刚德瑞瓦", 3, 3),
        [4555] = ("极乐鸟", 3, 3),
        [4556] = ("卢克洛塔", 3, 3),
        [6833] = ("巨大鳐", 3, 4),
        [6834] = ("伽马", 3, 4),
        [6835] = ("兀鲁忽乃朝鲁", 3, 4),
        [6836] = ("优昙婆罗花", 3, 4),
        [6837] = ("爬骨怪龙", 3, 4),
        [6838] = ("盐和光", 3, 4),
        [6839] = ("奥迦斯", 2, 4),
        [6840] = ("女王蜂", 2, 4),
        [6841] = ("弗克施泰因", 2, 4),
        [6842] = ("熔骨炎蝎", 2, 4),
        [6843] = ("马希沙", 2, 4),
        [6844] = ("泛光晶体", 2, 4),
        [6845] = ("船幽灵", 2, 4),
        [6846] = ("鬼观梦", 2, 4),
        [6847] = ("象魔修罗", 2, 4),
        [6848] = ("安迦达", 2, 4),
        [6849] = ("基里麦卡拉", 2, 4),
        [6850] = ("硕姆", 2, 4),
        [6851] = ("剑豪 刑具", 1, 4),
        [6852] = ("姑获鸟", 1, 4),
        [6853] = ("大太", 1, 4),
        [6854] = ("闪雷击 鱼雷", 1, 4),
        [6855] = ("俱利摩", 1, 4),
        [6856] = ("阿苏黄", 1, 4),
        [6857] = ("影中暗 雅弥尼", 1, 4),
        [6858] = ("奥祖鲁姆", 1, 4),
        [6859] = ("蛇仆蚂蜓", 1, 4),
        [6860] = ("布卡卜", 1, 4),
        [6861] = ("玛涅斯", 1, 4),
        [6862] = ("奇洼", 1, 4),
        [10270] = ("阿格拉俄珀", 3, 5),
        [10271] = ("泥人", 2, 5),
        [10272] = ("保尔迪雅", 2, 5),
        [10273] = ("杜莫伊", 1, 5),
        [10296] = ("伊休妲", 3, 5),
        [10297] = ("苏帕伊", 2, 5),
        [10298] = ("格拉斯曼", 2, 5),
        [10299] = ("启灵果", 1, 5),
        [10300] = ("帕查玛玛", 1, 5),
        [10322] = ("顾尼图", 3, 5),
        [10323] = ("卢莎卡", 2, 5),
        [10324] = ("巴力", 2, 5),
        [10325] = ("徒手抓鱼 基乌嘶·渊斯", 1, 5),
        [10326] = ("助祭大蟹", 1, 5),
        [10327] = ("狐首虺", 1, 5),
        [10355] = ("多智兽", 3, 5),
        [10356] = ("马利克巨人掌", 2, 5),
        [10357] = ("休格尔", 2, 5),
        [10358] = ("大井巨虫", 1, 5),
        [10359] = ("残虐杂技师", 1, 5),
        [10382] = ("戾虫", 3, 5),
        [10383] = ("纳克拉维", 2, 5),
        [10384] = ("纳里蓬", 2, 5),
        [10385] = ("浓毛兽", 1, 5),
        [10386] = ("伊兹帕帕洛特尔", 1, 5),
        [10418] = ("三合鸟儿", 1, 5),
        [10419] = ("不屈号", 1, 5),
        [10420] = ("小小杀手", 2, 5),
        [10421] = ("乌拉坎", 2, 5),
        [10422] = ("得到宽恕的叛乱", 4, 5), // 特殊处理SS
        [10634] = ("得到宽恕的炫学", 3, 5),
        [10755] = ("得到宽恕的流言", 1, 5),
        [13757] = ("沉思之物", 3, 6),
        [13758] = ("阿姆斯特朗", 3, 6),
        [13759] = ("尤兰", 2, 6),
        [13760] = ("伊塔总领", 2, 6),
        [13761] = ("凡·艾尔", 2, 6),
        [13775] = ("克尔", 4, 6), // 特殊处理SS
        [13787] = ("狭缝", 3, 6),
        [13788] = ("俄菲翁尼厄斯", 3, 6),
        [13789] = ("胡睹", 2, 6),
        [13790] = ("斯图希", 2, 6),
        [13791] = ("月面仙人刺女王", 2, 6),
        [13819] = ("瓣齿鲨", 2, 6),
        [13820] = ("须羯里婆", 2, 6),
        [13833] = ("密涅瓦", 2, 6),
        [13834] = ("布弗鲁", 3, 6),
        [13835] = ("慕斯公主", 2, 6),
        [13851] = ("固兰盖奇", 2, 6),
        [13936] = ("颇胝迦", 3, 6),
        [13937] = ("黑杨树精", 2, 6),
        [13938] = ("克尔的侍从", 1, 6),
        [13968] = ("草贤人", 1, 6),
        [13969] = ("哞哞", 1, 6),
        [13970] = ("金刚鸠摩罗", 1, 6),
        [13971] = ("伊罗婆缇", 1, 6),
        [13972] = ("战争贩子", 1, 6),
        [13973] = ("皇帝的玫瑰", 1, 6),
        [13975] = ("起源石", 1, 6),
        [13976] = ("尤姆卡克斯", 1, 6),
        [13977] = ("肖科莫", 1, 6),
        [13978] = ("等级作弊仪", 1, 6),
        [13979] = ("欧斯克·雷伊", 1, 6),
        [14168] = ("巨月蚤", 1, 6),
        [16749] = ("先驱勇士 阿提卡斯", 3, 7),
        [16750] = ("海休瓦拉", 2, 7),
        [16751] = ("多变装置", 2, 7),
        [16892] = ("幻煌鸟", 2, 7),
        [16946] = ("血鸣鼠", 2, 7),
        [16947] = ("内尤佐缇", 3, 7),
        [17301] = ("清扫者萨莉", 2, 7),
        [17302] = ("猫眼", 2, 7),
        [17303] = ("天气预报机器人", 3, 7),
        [17371] = ("艾海海陶瓦泡", 2, 7),
        [17372] = ("凯海尼海亚麦尤伊", 2, 7),
        [17373] = ("山谢亚", 3, 7),
        [17557] = ("疯狂龙舌兰", 1, 7),
        [17558] = ("卓伯卡布拉", 1, 7),
        [17559] = ("撞角头", 1, 7),
        [17560] = ("格沃佐拜柯拜", 1, 7),
        [17561] = ("绿叶飞花 哈杜加", 1, 7),
        [17562] = ("休提因别克", 1, 7),
        [17563] = ("绝妙圆扇刺", 1, 7),
        [17564] = ("乌克提纳", 1, 7),
        [17565] = ("斩首魔鸟", 1, 7),
        [17566] = ("高康特", 1, 7),
        [17567] = ("携宝精", 1, 7),
        [17568] = ("第十三个孩子", 1, 7),
        [17706] = ("厌忌之人 奇里格", 3, 7),
        [17707] = ("女王鹰蜂", 2, 7),
        [17708] = ("内丘奇霍", 2, 7),
        [17732] = ("水晶化身之王", 4, 7), // 特殊处理SS
        [17777] = ("水晶化身", 1, 7),
        [17791] = ("惊雨蟾蜍", 2, 7),
        [17792] = ("普库恰", 2, 7),
        [17794] = ("伊努索奇", 3, 7),
        // ...
    };
}
