using Sitecore.Data.Items;
using System.Collections.Generic;

namespace Website.Comparers
{
    public class ItemEqualityComparer : EqualityComparer<Item>
    {
        public override bool Equals(Item x, Item y)
        {
            if (ReferenceEquals(x, null)) {
                return ReferenceEquals(y, null);
            }
            else if (ReferenceEquals(y, null)) {
                return false;
            }

            return x.ID.Equals(y.ID);
        }

        public override int GetHashCode(Item item)
        {
            return item.ID.Guid.GetHashCode();
        }
    }
}