using Megaphonk.Common.Models;
using MemoryPack;
using System.Numerics;

namespace Megaphonk.Common.Messages
{
    [MemoryPackable]
    public partial class PlayerUpdate : IGameNetworkMessage
    {
        public uint ConnectionId { get; set; }
        public Vector3 Position { get; set; }
        public MovementState MovementState { get; set; } = new();

        public AnimatorState AnimatorState { get; set; } = new();

        /// <summary>
        /// Only sent when it changed, and every few seconds in case a packet was lost (this message is unreliable).
        /// Null otherwise: receivers keep the last one.
        /// </summary>
        public InventoryInfo? Inventory { get; set; }

        public string Name { get; set; } = "";
        public uint Hp { get; set; }
        public uint MaxHp { get; set; }
        //public uint Xp { get; set; }
        public uint Shield { get; set; }
        public uint MaxShield { get; set; }
    }

    [MemoryPackable]
    public partial class AnimatorState
    {
        public bool IsGrounded { get; set; }
        public bool IsMoving { get; set; }
        public bool IsIdle { get; set; }
        public bool IsGrinding { get; set; }

        public bool IsJumping { get; set; }
    }

    [MemoryPackable]
    public partial class MovementState
    {
        public QuantizedVector2 AxisInput { get; set; } = new();
        public QuantizedVector3 CameraForward { get; set; } = new();
        public QuantizedVector3 CameraRight { get; set; } = new();
    }
}
