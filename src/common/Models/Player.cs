using Megaphonk.Common.Messages;
using MemoryPack;

namespace Megaphonk.Common.Models
{
    [MemoryPackable]
    public partial class Player
    {
        public uint ConnectionId;
        public bool IsHost = false;
        public uint Character = 0;
        public string Skin = "";
        public bool IsReady = false;
        public string Name = "Player";
        public QuantizedVector3 Position = new();
        public AnimatorState AnimatorState { get; set; } = new();
        public MovementState MovementState { get; set; } = new();

        /// <summary>
        /// Never null in the stored players. Null on the wire (LobbyUpdates) when it did not change since the last one sent.
        /// </summary>
        public InventoryInfo? Inventory { get; set; } = new();

        public uint Hp = 100;
        public uint MaxHp = 100;
        //public uint Xp = 0;
        public uint Shield = 0;
        public uint MaxShield = 0;

        /// <summary>
        /// Copy to send with some fields left out (the nested objects are shared)
        /// </summary>
        public Player ShallowCopy() => (Player)MemberwiseClone();
    }
}
