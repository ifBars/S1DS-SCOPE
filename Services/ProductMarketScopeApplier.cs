#if SERVER || CLIENT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DedicatedServerMod.Organisations.Domain;
using DedicatedServerMod.Organisations.Utils;
using Newtonsoft.Json;
#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Product;
#else
using ScheduleOne.DevUtilities;
using ScheduleOne.Economy;
using ScheduleOne.Product;
#endif

namespace DedicatedServerMod.Organisations.Services;

internal static class ProductMarketScopeApplier
{
    private static readonly FieldInfo? ProductManagerIsAcceptingOrdersField = typeof(ProductManager).GetField("<IsAcceptingOrders>k__BackingField", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo? ProductManagerCurrentMixOperationField = typeof(ProductManager).GetField("<CurrentMixOperation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ProductManagerIsMixCompleteField = typeof(ProductManager).GetField("<IsMixComplete>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ProductManagerMixRecipesField = typeof(ProductManager).GetField("mixRecipes", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? ProductManagerCreateMixRecipeLogicMethod = typeof(ProductManager).GetMethod("RpcLogic___CreateMixRecipe_1410895574", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static void Apply(ProductMarketScopeRecord? state)
    {
        ProductManager? manager = NetworkSingleton<ProductManager>.Instance;
        if (manager == null || state == null)
        {
            return;
        }

        ApplyProductDefinitionList(manager, "DiscoveredProducts", state.DiscoveredProductIds);
        ApplyProductDefinitionList(manager, "ListedProducts", state.ListedProductIds);
        ApplyProductDefinitionList(manager, "FavouritedProducts", state.FavouritedProductIds);
        ApplyProductDefinitionList(manager, "createdProducts", state.CreatedProductIds);
        ApplyProductPrices(manager, state);
        ApplyProductMixRecipes(manager, state);
        ApplyProductContractReceipts(manager, state);
        ProductManagerIsAcceptingOrdersField?.SetValue(ProductManagerIsAcceptingOrdersField.IsStatic ? null : manager, state.IsAcceptingOrders);
        ProductManagerCurrentMixOperationField?.SetValue(manager, DeserializeProductMixOperation(state.CurrentMixOperationJson));
        ProductManagerIsMixCompleteField?.SetValue(manager, state.IsMixComplete);
    }

    private static void ApplyProductDefinitionList(ProductManager manager, string memberName, IEnumerable<string>? productIds)
    {
        object? value = GetProductManagerMemberValue(manager, memberName);
        if (value is not IList list)
        {
            return;
        }

        list.Clear();
        if (productIds == null)
        {
            return;
        }

        foreach (string productId in productIds.Where(productId => !string.IsNullOrWhiteSpace(productId)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            object? product = FindProductDefinition(manager, productId);
            if (product != null)
            {
                list.Add(product);
            }
        }
    }

    private static void ApplyProductPrices(ProductManager manager, ProductMarketScopeRecord state)
    {
        if (GetProductManagerMemberValue(manager, "ProductPrices") is not IDictionary productPrices)
        {
            return;
        }

        productPrices.Clear();
        if (state.PricesByProductId == null)
        {
            return;
        }

        foreach (KeyValuePair<string, float> pair in state.PricesByProductId)
        {
            object? product = FindProductDefinition(manager, pair.Key);
            if (product != null && !float.IsNaN(pair.Value) && !float.IsInfinity(pair.Value))
            {
                productPrices[product] = pair.Value;
            }
        }
    }

    private static void ApplyProductMixRecipes(ProductManager manager, ProductMarketScopeRecord state)
    {
        if (ProductManagerMixRecipesField?.GetValue(manager) is not IList mixRecipes)
        {
            return;
        }

        mixRecipes.Clear();
        if (state.MixRecipes == null || ProductManagerCreateMixRecipeLogicMethod == null)
        {
            return;
        }

        foreach (ProductMixRecipeScopeRecord recipe in state.MixRecipes)
        {
            if (recipe == null
                || string.IsNullOrWhiteSpace(recipe.ProductId)
                || string.IsNullOrWhiteSpace(recipe.MixerId)
                || string.IsNullOrWhiteSpace(recipe.OutputId))
            {
                continue;
            }

            ProductManagerCreateMixRecipeLogicMethod.Invoke(manager, new object?[] { null, recipe.ProductId, recipe.MixerId, recipe.OutputId });
        }
    }

    private static void ApplyProductContractReceipts(ProductManager manager, ProductMarketScopeRecord state)
    {
        if (manager.ContractReceipts == null)
        {
            return;
        }

        manager.ContractReceipts.Clear();
        if (state.ContractReceiptJson == null)
        {
            return;
        }

        foreach (string receiptJson in state.ContractReceiptJson)
        {
            ContractReceipt? receipt = DeserializeProductContractReceipt(receiptJson);
            if (receipt != null && !manager.ContractReceipts.AsManagedEnumerable().Any(existing => existing.ReceiptId == receipt.ReceiptId))
            {
                manager.ContractReceipts.Add(receipt);
            }
        }
    }

    private static object? FindProductDefinition(ProductManager manager, string productId)
    {
        if (string.IsNullOrWhiteSpace(productId) || GetProductManagerMemberValue(manager, "AllProducts") is not IEnumerable products)
        {
            return null;
        }

        foreach (object product in products)
        {
            if (string.Equals(GetProductId(product), productId, StringComparison.OrdinalIgnoreCase))
            {
                return product;
            }
        }

        return null;
    }

    private static NewMixOperation? DeserializeProductMixOperation(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<NewMixOperation>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ContractReceipt? DeserializeProductContractReceipt(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<ContractReceipt>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetProductId(object? product)
    {
        if (product == null)
        {
            return string.Empty;
        }

        Type type = product.GetType();
        PropertyInfo? property = type.GetProperty("ID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.GetValue(product) is string propertyValue)
        {
            return propertyValue;
        }

        FieldInfo? field = type.GetField("ID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(product) as string ?? string.Empty;
    }

    private static object? GetProductManagerMemberValue(ProductManager manager, string memberName)
    {
        Type type = typeof(ProductManager);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo? field = type.GetField(memberName, flags);
        if (field != null)
        {
            object? target = field.IsStatic ? null : manager;
            return field.GetValue(target);
        }

        PropertyInfo? property = type.GetProperty(memberName, flags);
        if (property != null)
        {
            MethodInfo? getter = property.GetGetMethod(nonPublic: true);
            object? target = getter?.IsStatic == true ? null : manager;
            return property.GetValue(target);
        }

        return null;
    }
}
#endif
