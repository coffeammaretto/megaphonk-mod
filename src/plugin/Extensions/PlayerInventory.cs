using MegabonkTogether.Common.Models;

namespace MegabonkTogether.Extensions
{
    internal static class PlayerInventoryExtensions
    {
        public static InventoryInfo ToInventoryInfos(this PlayerInventory playerInventory)
        {
            var inventoryInfo = new InventoryInfo();
            foreach (var weaponKey in playerInventory.weaponInventory.weapons.Keys)
            {
                var weaponInfo = new WeaponInfo
                {
                    EWeapon = (uint)weaponKey,
                    Level = (uint)playerInventory.weaponInventory.weapons[weaponKey].level
                };
                inventoryInfo.WeaponInfos.Add(weaponInfo);
            }

            foreach (var tomeKey in playerInventory.tomeInventory.tomeLevels.Keys)
            {
                var tomeInfo = new TomeInfo
                {
                    ETome = (uint)tomeKey,
                    Level = (uint)playerInventory.tomeInventory.tomeLevels[tomeKey]
                };
                inventoryInfo.TomeInfos.Add(tomeInfo);
            }
            return inventoryInfo;
        }

        /// <summary>
        /// Same weapons and tomes at the same levels, in the same order (a different order only costs a resend)
        /// </summary>
        public static bool HasSameContent(this InventoryInfo inventory, InventoryInfo other)
        {
            if (inventory == null || other == null)
            {
                return inventory == other;
            }

            if (inventory.WeaponInfos.Count != other.WeaponInfos.Count || inventory.TomeInfos.Count != other.TomeInfos.Count)
            {
                return false;
            }

            using (var a = inventory.WeaponInfos.GetEnumerator())
            using (var b = other.WeaponInfos.GetEnumerator())
            {
                while (a.MoveNext() && b.MoveNext())
                {
                    if (a.Current.EWeapon != b.Current.EWeapon || a.Current.Level != b.Current.Level)
                    {
                        return false;
                    }
                }
            }

            using (var a = inventory.TomeInfos.GetEnumerator())
            using (var b = other.TomeInfos.GetEnumerator())
            {
                while (a.MoveNext() && b.MoveNext())
                {
                    if (a.Current.ETome != b.Current.ETome || a.Current.Level != b.Current.Level)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
