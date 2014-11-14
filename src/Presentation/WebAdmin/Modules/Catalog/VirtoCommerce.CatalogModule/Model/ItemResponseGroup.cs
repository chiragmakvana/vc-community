﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VirtoCommerce.CatalogModule.Model
{
	[Flags]
	public enum ItemResponseGroup
	{
		ItemInfo = 0,
		ItemAssets = 1 << 1,
		ItemProperties = 1 << 2,
		ItemAssociations = 1 << 3,
		ItemEditorialReviews = 1 << 4,
		Variations = 1 << 5,
		ItemSmall = ItemInfo | ItemAssets | ItemProperties,
		ItemMedium = ItemInfo | ItemAssets | ItemProperties | ItemAssociations | ItemEditorialReviews,
		ItemLarge = ItemInfo | ItemAssets | ItemProperties | ItemAssociations | ItemEditorialReviews | Variations
	}
}