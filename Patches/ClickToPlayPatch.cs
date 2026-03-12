using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace ClickToPlay.Patches;

/// <summary>
/// 实现单击出牌：卡牌被拿起（进入拖拽流程）之前，若无需手动选目标，直接出牌。
///
/// 【时序】
/// 每次点击触发两次 StartCardPlay：
///   第1次：鼠标按下 → HolderMouseClicked → StartCardPlay → 满足条件则直接出牌，跳过原方法，并设 _skipNext=true
///   第2次：鼠标松开 → Pressed → StartCardPlay → 检测到 _skipNext，清除后直接跳过，防止重复出牌
///
/// 【无需手动选目标的判断】
///   1. None/Self/AllEnemies/AllAllies/RandomEnemy/Osty/TargetedNoCreature → 直接出（target=null）
///   2. AnyEnemy + 只有一个可击打的敌人 → 自动选那个敌人
///   3. AnyAlly + 只有一个存活的非自身盟友 → 自动选那个盟友
///   4. AnyPlayer + 只有一个存活的玩家 → 自动选那个玩家
/// </summary>
[HarmonyPatch(typeof(NPlayerHand), "StartCardPlay")]
public static class ClickToPlayPatch
{
    // 第1次出牌后设为 true，让第2次（Pressed 触发）的调用直接跳过
    private static bool _skipNext;

    private static bool TryGetAutoPlayTarget(CardModel card, out Creature? target)
    {
        target = null;
        if (card.CombatState == null)
            return false;

        CombatState combatState = card.CombatState;

        switch (card.TargetType)
        {
            case TargetType.None:
            case TargetType.Self:
            case TargetType.AllEnemies:
            case TargetType.AllAllies:
            case TargetType.RandomEnemy:
            case TargetType.Osty:
            case TargetType.TargetedNoCreature:
                return true;

            case TargetType.AnyEnemy:
            {
                IReadOnlyList<Creature> enemies = combatState.HittableEnemies;
                if (enemies.Count == 1)
                {
                    target = enemies[0];
                    return true;
                }
                return false;
            }

            case TargetType.AnyAlly:
            {
                Creature ownerCreature = card.Owner.Creature;
                List<Creature> allies = combatState.PlayerCreatures
                    .Where(c => c.IsAlive && c != ownerCreature)
                    .ToList();
                if (allies.Count == 1)
                {
                    target = allies[0];
                    return true;
                }
                return false;
            }

            case TargetType.AnyPlayer:
            {
                List<Creature> players = combatState.PlayerCreatures
                    .Where(c => c.IsAlive)
                    .ToList();
                if (players.Count == 1)
                {
                    target = players[0];
                    return true;
                }
                return false;
            }

            default:
                return false;
        }
    }

    [HarmonyPrefix]
    public static bool Prefix(NHandCardHolder holder, bool startedViaShortcut)
    {
        // 手柄模式不干预
        if (NControllerManager.Instance?.IsUsingController == true)
            return true;

        // 第2次调用（Pressed 松开信号触发）：跳过，防止重复出牌
        if (_skipNext)
        {
            _skipNext = false;
            return false;
        }

        CardModel? card = holder.CardModel;
        if (card == null)
            return true;

        if (!TryGetAutoPlayTarget(card, out Creature? target))
            return true; // 不满足自动出牌条件，走原拖拽流程

        if (!card.CanPlayTargeting(target))
            return true; // 牌出不了，走原流程显示错误提示

        // 直接出牌，并标记跳过随后的第2次调用
        card.TryManualPlay(target);
        _skipNext = true;
        return false; // 跳过原方法（不启动拖拽）
    }
}
