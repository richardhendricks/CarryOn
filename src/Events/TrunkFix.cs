using CarryOn.API.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Events
{
    public class TrunkFix : ICarryEvent
    {
        public void Init(CarrySystem carrySystem)
        {
            if (carrySystem.Api.Side != EnumAppSide.Server) return;

            carrySystem.CarryEvents.OnRestoreEntityBlockData += OnRestoreEntityBlockData;
        }

        public void OnRestoreEntityBlockData(BlockEntity blockEntity, ITreeAttribute blockEntityData, bool dropped)
        {
            if (dropped && blockEntity.Block.Class == "BlockGenericTypedContainerTrunk")
            {
                // Fix dropped trunk angle
                blockEntityData.SetFloat("meshAngle", -90 * GameMath.DEG2RAD);
            }
        }
    }
}