using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace CalWotGMPFix.Common;

// IL edits kept free of tModLoader types so they can be tested offline against the real NoxusBoss.dll.
public static class ILPatches
{
    // AvatarOfEmptiness.ReceiveExtraAI reads the telegraph list like this:
    //     int telegraphCount = reader.ReadInt32();
    //     TwinkleSmashDetails = new(telegraphCount);           // sets capacity, Count stays 0
    //     for (int i = 0; i < TwinkleSmashDetails.Count; i++)  // so this never runs
    //         TwinkleSmashDetails.Add(TwinkleSmashTelegraphSet.Read(reader));
    // The telegraph bytes are never consumed, so the state-stack read after it reads garbage.
    // Fix: make the loop bound telegraphCount instead of TwinkleSmashDetails.Count.
    public static void FixTelegraphReadLoop(ILContext il)
    {
        ILCursor c = new(il);
        int countLocal = -1;

        if (!c.TryGotoNext(MoveType.After,
            i => i.MatchLdloc(out countLocal),
            i => i.MatchNewobj(out MethodReference ctor) && ctor.DeclaringType.Name.StartsWith("List`1"),
            i => i.MatchStfld(out FieldReference f) && f.Name == "TwinkleSmashDetails"))
            throw new Exception("Could not find 'TwinkleSmashDetails = new(telegraphCount)'.");

        if (!c.TryGotoNext(MoveType.After,
            i => i.MatchLdfld(out FieldReference f) && f.Name == "TwinkleSmashDetails",
            i => i.MatchCallvirt(out MethodReference m) && m.Name == "get_Count"))
            throw new Exception("Could not find the telegraph read loop condition.");

        // Make sure this is the loop that reads telegraph sets, not something else.
        ILCursor check = c.Clone();
        bool feedsReadLoop = check.TryGotoPrev(i => i.MatchCall(out MethodReference m) && m.Name == "Read" && m.DeclaringType.Name == "TwinkleSmashTelegraphSet");
        if (!feedsReadLoop)
            throw new Exception("Loop condition found, but it is not the telegraph read loop.");

        // Stack: [Count] -> [telegraphCount]
        c.Emit(OpCodes.Pop);
        c.Emit(OpCodes.Ldloc, il.Body.Variables[countLocal]);
    }

    // The FrostScreenSmash skip is a compiler-generated lambda: () => Main.netMode != NetmodeID.SinglePlayer
    // It is the only lambda in LoadState_FrostScreenSmash that reads Main.netMode and calls nothing.
    public static bool IsNetModeOnlyLambda(Mono.Cecil.Cil.MethodBody body)
    {
        var ins = body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToList();
        if (ins.Count == 0 || ins.Count > 10)
            return false;
        if (ins.Any(i => i.OpCode.FlowControl == FlowControl.Call))
            return false;
        bool readsNetMode = ins.Any(i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference f && f.Name == "netMode" && f.DeclaringType.FullName == "Terraria.Main");
        return readsNetMode && ins[^1].OpCode == OpCodes.Ret;
    }

    // Replace the lambda body with a call to our own decision method (bool()).
    public static bool ReplaceNetModeLambda(ILContext il, MethodInfo replacement)
    {
        if (!IsNetModeOnlyLambda(il.Body))
            return false;

        il.Instrs.Clear();
        ILCursor c = new(il);
        c.Emit(OpCodes.Call, replacement);
        c.Emit(OpCodes.Ret);
        return true;
    }

    // Inserts "[ldarg npcArg]; call callback" before every ret. Anything that jumped to a ret now jumps to
    // the inserted code (MoveType.AfterLabel), so every exit path runs the callback.
    public static void CallBeforeEveryReturn(ILContext il, MethodInfo callback, int npcArgIndex)
    {
        ILCursor c = new(il);
        int count = 0;
        while (c.TryGotoNext(MoveType.AfterLabel, i => i.OpCode == OpCodes.Ret))
        {
            if (npcArgIndex >= 0)
                c.Emit(OpCodes.Ldarg, il.Method.Parameters[npcArgIndex]);
            c.Emit(OpCodes.Call, callback);
            c.Index++; // step past the ret
            count++;
        }
        if (count == 0)
            throw new Exception("no return found");
    }
}
