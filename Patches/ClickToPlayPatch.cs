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
/// 拦截 NPlayerHand.StartCardPlay，当该卡牌无需手动选择目标时，
/// 直接调用 TryManualPlay 出牌，跳过拖拽流程。
///
/// "无需手动选目标"的判断：
///   1. 目标类型不是单选类型（IsSingleTarget() == false），例如 None/Self/AllEnemies/AllAllies/RandomEnemy/Osty/TargetedNoCreature
///      但注意 Self / TargetedNoCreature 也属于 IsSingleTarget，所以用更直接的判断
///   2. TargetType == AnyEnemy 且当前只有一个可击打的敌人
///   3. TargetType == AnyAlly 且当前只有一个存活的可选盟友（非自身玩家角色）
/// </summary>
[HarmonyPatch(typeof(NPlayerHand))]
public static class NPlayerHandPatch
{
    /// <summary>
    /// 判断这张牌是否可以自动出牌（无需拖拽选目标）。
    /// 如果可以，返回要传给 TryManualPlay 的目标（无目标时为 null）。
    /// 如果不能自动出牌，返回 false。
    /// </summary>
    private static bool TryGetAutoPlayTarget(CardModel card, out Creature? target)
    {
        target = null;

        if (card.CombatState == null)
            return false;

        CombatState combatState = card.CombatState;
        TargetType targetType = card.TargetType;

        switch (targetType)
        {
            // 无目标 / 自己 / 全部敌人 / 全部友方 / 随机敌人 / Osty / 无生物目标
            // 这些情况下拖到出牌区后原本就不需要手动指向目标，但我们可以直接出牌
            case TargetType.None:
            case TargetType.Self:
            case TargetType.AllEnemies:
            case TargetType.AllAllies:
            case TargetType.RandomEnemy:
            case TargetType.Osty:
            case TargetType.TargetedNoCreature:
                target = null;
                return true;

            // 指定单个敌人：只有在敌人唯一时自动出牌
            case TargetType.AnyEnemy:
            {
                IReadOnlyList<Creature> hittableEnemies = combatState.HittableEnemies;
                if (hittableEnemies.Count == 1)
                {
                    target = hittableEnemies[0];
                    return true;
                }
                return false;
            }

            // 指定单个盟友（多人游戏）：只有在可选盟友唯一时自动出牌
            case TargetType.AnyAlly:
            {
                Creature ownerCreature = card.Owner.Creature;
                // AnyAlly 排除自身，找其他存活的可选盟友
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

            // AnyPlayer 主要用于药水，卡牌一般不用，但以防万一：单人时自动选自己
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
    [HarmonyPatch("StartCardPlay")]
    public static bool StartCardPlay_Prefix(NHandCardHolder holder, bool startedViaShortcut)
    {
        // 只在鼠标模式下拦截（手柄模式有自己的逻辑）
        if (NControllerManager.Instance?.IsUsingController == true)
            return true; // 让原方法继续执行

        CardModel? card = holder.CardModel;
        if (card == null)
            return true;

        // 检查是否可以自动出牌
        if (!TryGetAutoPlayTarget(card, out Creature? target))
            return true; // 需要选目标，走原来的拖拽流程

        // 验证这张牌确实可以出（包括法力值、CanPlay 检查等）
        if (!card.CanPlayTargeting(target))
        {
            // 模拟原方法中 CannotPlayThisCardFtueCheck 的调用
            // 通过反射调用私有方法（或通过 NCardPlay 的静态方法）
            // 这里用 TryManualPlay 本身失败来处理即可，原流程会显示提示
            return true;
        }

        // 直接出牌
        card.TryManualPlay(target);
        return false; // 跳过原方法
    }
}

