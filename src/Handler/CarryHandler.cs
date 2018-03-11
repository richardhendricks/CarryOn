using CarryCapacity.Network;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryCapacity.Handler
{
	/// <summary>
	///   Takes care of core CarryCapacity handling, such as listening to events,
	///   picking up and placing blocks, as well as sending and handling messages.
	/// </summary>
	public class CarryHandler
	{
		public const float PLACE_SPEED_MODIFIER = 0.75F;
		
		private CurrentAction _action   = CurrentAction.None;
		private BlockPos _selectedBlock = null;
		
		private CarryCapacityMod Mod { get; }
		
		public CarryHandler(CarryCapacityMod mod)
			=> Mod = mod;
		
		public void InitClient()
		{
			Mod.MouseHandler.OnRightMousePressed  += OnPress;
			Mod.MouseHandler.OnRightMouseHeld     += OnHold;
			Mod.MouseHandler.OnRightMouseReleased += OnRelease;
		}
		
		public void InitServer()
		{
			Mod.ServerChannel
				.SetMessageHandler<PickUpMessage>(OnPickUpMessage)
				.SetMessageHandler<PlaceDownMessage>(OnPlaceDownMessage);
		}
		
		
		public void OnPress()
		{
			var world     = Mod.ClientAPI.World;
			var player    = world.Player;
			var carried   = player.Entity.GetCarried();
			var selection = player.CurrentBlockSelection;
			_selectedBlock = selection?.Position;
			if (_selectedBlock == null) return;
			if (carried == null) {
				// Pick up a block. Ensure it's carryable.
				if (!world.BlockAccessor.GetBlock(_selectedBlock).IsCarryable()) return;
				_action = CurrentAction.PickUp;
			} else {
				// Place down a block. Make sure it's
				// put on a solid top face of a block.
				if (!CanPlace(world, selection, carried)) return;
				_action = CurrentAction.PlaceDown;
			}
			OnHold(0.0F);
		}
		
		public void OnHold(float time)
		{
			if (_action == CurrentAction.None) return;
			var player = Mod.ClientAPI.World.Player;
			
			// TODO: Don't run any of this while in a GUI.
			// TODO: Only allow close blocks to be picked up.
			// TODO: Don't allow the block underneath to change?
			
			var isSneaking    = player.Entity.Controls.Sneak;
			var isEmptyHanded = player.Entity.RightHandItemSlot.Empty;
			// Only pick up or place down if sneaking and empty handed.
			if (!isSneaking || !isEmptyHanded)
				{ OnRelease(); return; }
			
			// Make sure the player is still looking at the same block.
			var selection = player.CurrentBlockSelection;
			if (!_selectedBlock.Equals(selection?.Position))
				{ OnRelease(); return; }
			
			var carried = player.Entity.GetCarried();
			// Ensure the player hasn't in the meantime
			// picked up / placed down something somehow.
			if ((_action == CurrentAction.PickUp) == (carried != null))
				{ OnRelease(); return; }
			
			// Get the block behavior from either the block
			// to be picked up or the currently carried block.
			var behavior = ((_action == CurrentAction.PickUp)
					? player.Entity.World.BlockAccessor.GetBlock(_selectedBlock)
					: carried.Block
				).GetBehaviorOrDefault(BlockBehaviorCarryable.DEFAULT);
			
			var requiredTime = behavior.InteractDelay;
			if (_action == CurrentAction.PlaceDown)
				requiredTime *= PLACE_SPEED_MODIFIER;
			
			var progress = (time / requiredTime);
			Mod.HudOverlayRenderer.CircleProgress = progress;
			if (progress <= 1.0F) return;
			
			if (_action == CurrentAction.PickUp) {
				// If not currently carrying a block, see if we can pick one up.
				if (player.Entity.Carry(selection.Position))
					Mod.ClientChannel.SendPacket(new PickUpMessage(selection.Position));
			} else {
				// If already carrying a block, see if we can place it down.
				if (PlaceDown(player, carried, selection))
					Mod.ClientChannel.SendPacket(new PlaceDownMessage(selection));
			}
			OnRelease();
		}
		
		public void OnRelease()
		{
			_action = CurrentAction.None;
			Mod.HudOverlayRenderer.CircleVisible = false;
		}
		
		
		public static void OnPickUpMessage(IPlayer player, PickUpMessage message)
		{
			// FIXME: Do at least some validation of this data.
			
			var isSneaking    = player.Entity.Controls.Sneak;
			var isEmptyHanded = player.Entity.RightHandItemSlot.Empty;
			var carried       = player.Entity.GetCarried();
			
			if (!isSneaking || !isEmptyHanded || (carried != null)
				|| !player.Entity.Carry(message.Position))
					InvalidCarry(player, message.Position);
		}
		
		public static void OnPlaceDownMessage(IPlayer player, PlaceDownMessage message)
		{
			// FIXME: Do at least some validation of this data.
			
			var isSneaking    = player.Entity.Controls.Sneak;
			var isEmptyHanded = player.Entity.RightHandItemSlot.Empty;
			var carried       = player.Entity.GetCarried();
			
			if (!isSneaking || !isEmptyHanded || (carried == null)
				|| !PlaceDown(player, carried, message.Selection))
					InvalidCarry(player, message.Selection.Position);
		}
		
		public static bool CanPlace(IWorldAccessor world, BlockSelection selection,
		                            CarriedBlock carried)
		{
			var clickedBlock = world.BlockAccessor.GetBlock(selection.Position);
			// If clicked block is replacable, check block below instead.
			if (clickedBlock.IsReplacableBy(carried.Block)) {
				clickedBlock = world.BlockAccessor.GetBlock(selection.Position.DownCopy());
			// Otherwise make sure that the block was clicked on the top side.
			} else if (selection.Face != BlockFacing.UP) return false;
			return clickedBlock.SideSolid[BlockFacing.UP.Index];
		}
		
		public static bool PlaceDown(IPlayer player, CarriedBlock carried,
		                             BlockSelection selection)
		{
			if (!CanPlace(player.Entity.World, selection, carried)) return false;
			var clickedBlock = player.Entity.World.BlockAccessor.GetBlock(selection.Position);
			
			// Clone the selection, because we don't
			// want to affect what is sent to the server.
			selection = selection.Clone();
			
			if (clickedBlock.IsReplacableBy(carried.Block)) {
				selection.Face = BlockFacing.UP;
				selection.HitPosition.Y = 1.0;
			} else {
				selection.Position.Up();
				selection.DidOffset = true;
			}
			
			return player.PlaceCarried(selection);
		}
		
		/// <summary> Called when a player picks up or places down an invalid block,
		///           requiring it to get notified about the action being rejected. </summary>
		private static void InvalidCarry(IPlayer player, BlockPos pos)
		{
			player.Entity.World.BlockAccessor.MarkBlockDirty(pos);
			player.Entity.WatchedAttributes.MarkPathDirty(CarriedBlock.ATTRIBUTE_ID);
		}
		
		private enum CurrentAction
		{
			None,
			PickUp,
			PlaceDown
		}
	}
}
