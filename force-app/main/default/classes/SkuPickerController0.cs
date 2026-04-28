public with sharing class SkuPickerController {

    // =========================
    // 1. BUILD MATRIX (MASTER)
    // =========================
    @AuraEnabled(cacheable=true)
    public static List<Product_SKU__c> bindProductSKU(Id orderItemId){

        if(orderItemId == null){
            throw new AuraHandledException('OrderItem Id missing');
        }

        OrderItem oi = [ SELECT Id, Product2Id FROM OrderItem WHERE Id = :orderItemId LIMIT 1];
        return [
            SELECT Id, Name, Diameter__c, Length__c, Product__c, IsActive__c
            FROM Product_SKU__c
            WHERE Product__c = :oi.Product2Id
            ORDER BY Diameter__c, Length__c
        ];
    }


    // =========================
    // 2. GET TRANSACTION DATA
    // =========================
    @AuraEnabled(cacheable=true)
    public static List<Order_SKU__c> getOrderSKU(Id orderItemId){

        if(orderItemId == null){
            throw new AuraHandledException('OrderItem Id missing');
        }

        return [
            SELECT Id,
                   ProductSKU__c,
                   Quantity__c,
                   UnitPrice__c,
                   Total__c,
                   UniqueKey__c
            FROM Order_SKU__c
            WHERE OrderNumber__c = :orderItemId
        ];
    }


    // =========================
    // 3. SAVE MATRIX (INSERT + UPDATE + DELETE)
    // =========================
    @AuraEnabled
    public static void saveMatrix(Id orderItemId, String dataJson){

        if(orderItemId == null){
            throw new AuraHandledException('Order Item is missing');
        }

        if(String.isBlank(dataJson)){
            throw new AuraHandledException('No data to save');
        }

        // 🔹 Deserialize
        List<SKUWithQtyWrapper> data =
            (List<SKUWithQtyWrapper>) JSON.deserialize(dataJson, List<SKUWithQtyWrapper>.class);

        // 🔹 Get OrderItem pricing
        OrderItem oi = [
            SELECT Id, UnitPrice, ListPrice, TotalPrice, Quantity
            FROM OrderItem
            WHERE Id = :orderItemId
            LIMIT 1
        ];

        Decimal unitPrice;
        if (oi.UnitPrice != null && oi.UnitPrice > 0) {
            unitPrice = oi.UnitPrice;
        } else if (oi.ListPrice != null && oi.ListPrice > 0) {
            unitPrice = oi.ListPrice;
        } else if (oi.TotalPrice != null && oi.Quantity != null && oi.Quantity > 0) {
            unitPrice = oi.TotalPrice / oi.Quantity;
        } else {
            unitPrice = 0;
        }

        // 🔹 Collect SKU Ids
        Set<Id> skuIds = new Set<Id>();
        for (SKUWithQtyWrapper row : data) {
            if (row.skuId != null) {
                skuIds.add(row.skuId);
            }
        }

        // 🔹 Fetch SKU Master Data
        Map<Id, Product_SKU__c> skuMap = new Map<Id, Product_SKU__c>(
            [SELECT Id, Product_Name__c, Item_Code__c
             FROM Product_SKU__c
             WHERE Id IN :skuIds]
        );

        // 🔹 Fetch Existing Records (for DELETE / UPDATE)
        Map<String, Order_SKU__c> existingMap = new Map<String, Order_SKU__c>();

        for (Order_SKU__c rec : [
            SELECT Id, UniqueKey__c
            FROM Order_SKU__c
            WHERE OrderNumber__c = :orderItemId
        ]) {
            existingMap.put(rec.UniqueKey__c, rec);
        }

        List<Order_SKU__c> toUpsert = new List<Order_SKU__c>();
        List<Order_SKU__c> toDelete = new List<Order_SKU__c>();

        // 🔹 Track incoming keys
        Set<String> incomingKeys = new Set<String>();

        for (SKUWithQtyWrapper row : data) {

            if (row.skuId == null) continue;
			
            // 🔹 Ensure integer quantity
            if (row.quantity != null) {
                row.quantity = row.quantity.setScale(0, RoundingMode.DOWN);
            }
            
            String key = orderItemId + '_' + row.skuId;
            incomingKeys.add(key);

            // 🔥 DELETE case
            if (row.quantity == null || row.quantity <= 0) {
                if (existingMap.containsKey(key)) {
                    toDelete.add(existingMap.get(key));
                }
                continue;
            }

            Product_SKU__c sku = skuMap.get(row.skuId);
            
            String skuName = (sku != null) ? sku.Product_Name__c : 'Product Name';
			String skuItem = (sku != null) ? sku.Item_Code__c : 'SKU Code';
            
            Decimal total = unitPrice * row.quantity;

            toUpsert.add(new Order_SKU__c(
                Name             = skuName,
                UniqueKey__c     = orderItemId + '_' + row.skuId,
                OrderNumber__c   = orderItemId,
                ProductSKU__c    = skuItem,
                Quantity__c      = row.quantity,
                UnitPrice__c     = unitPrice,
                Total__c         = total
            ));
        }

        // 🔹 DELETE missing rows
        for(String key : existingMap.keySet()){
            if(!incomingKeys.contains(key)){
                toDelete.add(existingMap.get(key));
            }
        }

        // 🔹 DML Operations
        if(!toUpsert.isEmpty()){
            upsert toUpsert UniqueKey__c;
        }

        if(!toDelete.isEmpty()){
            delete toDelete;
        }
        
        // 🔹 Calculate total SKU quantity
        Decimal totalQty = 0;
        
        for(Order_SKU__c skuRec : [
            SELECT Quantity__c 
            FROM Order_SKU__c 
            WHERE OrderNumber__c = :orderItemId
        ]) {
            totalQty += skuRec.Quantity__c != null ? skuRec.Quantity__c : 0;
        }
        
        // 🔹 Update OrderItem
        OrderItem oiToUpdate = new OrderItem(
            Id = orderItemId,
            ProductSKUCount__c = totalQty
        );
        
        update oiToUpdate;
    }

    @AuraEnabled(cacheable=true)
    public static Integer getOrderQuantity(Id orderItemId){
        OrderItem oi = [SELECT Quantity FROM OrderItem WHERE Id = :orderItemId LIMIT 1];    
        return (oi.Quantity != null) ? Integer.valueOf(oi.Quantity) : 0;
    }
    
    @AuraEnabled(cacheable=true)
    public static Boolean isQuantityLocked(Id orderItemId){
        OrderItem oi = [SELECT OrderId FROM OrderItem WHERE Id = :orderItemId LIMIT 1];    
        Order ord = [SELECT QuantityLock__c FROM Order WHERE Id = :oi.OrderId LIMIT 1];    
        return ord.QuantityLock__c;
    }
    
    // =========================
    // WRAPPER
    // =========================
    public class SKUWithQtyWrapper {
        @AuraEnabled public Id skuId;
        @AuraEnabled public Decimal quantity;
    }
    
    
   	@AuraEnabled(cacheable=true)
    public static Map<Id, SKUExtraWrapper> getSKUExtraData(Id accountId){
    
        Map<Id, SKUExtraWrapper> result = new Map<Id, SKUExtraWrapper>();
        Map<String, Id> skuCodeToIdMap = new Map<String, Id>();
    
        try {
            for(Product_SKU__c sku : [ SELECT Id, Item_Code__c FROM Product_SKU__c WHERE Item_Code__c != NULL ]){
                skuCodeToIdMap.put(sku.Item_Code__c, sku.Id);
            }
        } catch(Exception e){
            System.debug('SKU MAP ERROR: ' + e.getMessage());
        }
    
        // =========================
        // 1. InHand (FIXED)
        // =========================
        try {
            
            // 1️⃣ Order quantities
            Map<String, Decimal> orderQtyBySku = new Map<String, Decimal>();
        
            for (AggregateResult ar : [
                SELECT ProductSKU__c sku, SUM(Quantity__c) qty FROM Order_SKU__c WHERE ProductSKU__c != NULL
                AND OrderNumber__r.Order.AccountId = :accountId GROUP BY ProductSKU__c
            ]) {
                orderQtyBySku.put(
                    (String) ar.get('sku'),
                    (Decimal) ar.get('qty')
                );
            }
        
        
            // 2️⃣ Secondary quantities
            Map<String, Decimal> secondaryQtyBySku = new Map<String, Decimal>();
        
            for (AggregateResult ar : [
                SELECT Product_REF_No__c sku, COUNT(Id) qty FROM Secondary_Sales__c WHERE Distributor__c = :String.valueOf(accountId)
                AND Product_REF_No__c != NULL GROUP BY Product_REF_No__c
            ]) {
                secondaryQtyBySku.put(
                    (String) ar.get('sku'),
                    (Decimal) ar.get('qty')
                );
            }
        
            Set<String> skuCodes = new Set<String>();
            skuCodes.addAll(orderQtyBySku.keySet());
            skuCodes.addAll(secondaryQtyBySku.keySet());
        
            for (String skuCode : skuCodes) {
        
            Decimal orderQty = orderQtyBySku.containsKey(skuCode) ? orderQtyBySku.get(skuCode) : 0;        
            Decimal secondaryQty = secondaryQtyBySku.containsKey(skuCode) ? secondaryQtyBySku.get(skuCode) : 0;
        
            Decimal odrValue = orderQty - secondaryQty;
                
            if (skuCodeToIdMap.containsKey(skuCode)) {
            	Id skuId = skuCodeToIdMap.get(skuCode);                            
                if (!result.containsKey(skuId)) {
                	result.put(skuId, new SKUExtraWrapper());
                }
                SKUExtraWrapper wrapper = result.get(skuId);
                wrapper.inHand =  odrValue;
              }
            }
        
        } catch (Exception e) {
            System.debug('ODR Calculation ERROR: ' + e.getMessage());
        }
        
    
        // =========================
        // 2. BackOrder
        // =========================
        Set<Id> orderItemIds = new Set<Id>();
    
        for(OrderItem oi : [SELECT Id FROM OrderItem WHERE Order.AccountId = :accountId]){
            orderItemIds.add(oi.Id);
        }
        
        try {
            for(AggregateResult ar : [
                SELECT ProductSKU__c sku,
                       SUM(RequestQuantity__c) reqQty,
                       SUM(Quantity__c) actQty
                FROM Order_SKU__c
                WHERE OrderNumber__c IN :orderItemIds 
                GROUP BY ProductSKU__c
            ]){
               String skuCode = (String) ar.get('sku');
               if(skuCodeToIdMap.containsKey(skuCode)){
                    Id skuId = skuCodeToIdMap.get(skuCode);
            
                    Decimal req = (Decimal) ar.get('reqQty');
                    Decimal act = (Decimal) ar.get('actQty');
            
                    Decimal reqVal = req != null ? req : 0;
                    Decimal actVal = act != null ? act : 0;
                    
                    Decimal backOrder = reqVal - actVal;
            
                    if(backOrder < 0){
                        backOrder = 0;
                    }
            
                    if(!result.containsKey(skuId)){
                        result.put(skuId, new SKUExtraWrapper());
                    }
            
                    result.get(skuId).backOrder = backOrder;
                }
            }
        } catch(Exception e){
            System.debug('BackOrder ERROR: ' + e.getMessage());
        }
    
        // =========================
        // 3. Proposed
        // =========================
        try {
            for(AggregateResult ar : [
                SELECT ProductSKU__c sku,
                       MAX(MinimumSKU__c) minQty
                FROM ProductSKURule__c
                GROUP BY ProductSKU__c
            ]){
                Id skuId = (Id) ar.get('sku');
                Decimal minQty = (Decimal) ar.get('minQty');
    
                if(!result.containsKey(skuId)){
                    result.put(skuId, new SKUExtraWrapper());
                }
    
                result.get(skuId).proposed = minQty != null ? minQty : 0;
            }
        } catch(Exception e){
            System.debug('Proposed ERROR: ' + e.getMessage());
        }
    
        return result;
    }
    
    // =========================
    // WRAPPER
    // =========================
    public class SKUExtraWrapper {
        @AuraEnabled public Decimal inHand = 0;
        @AuraEnabled public Decimal backOrder = 0;
        @AuraEnabled public Decimal proposed = 0;
    }
    
    @AuraEnabled(cacheable=true)
    public static OrderInfoWrapper getOrderInfo(Id orderItemId){
        OrderItem oi = [
            SELECT Id, Quantity, OrderId
            FROM OrderItem
            WHERE Id = :orderItemId
            LIMIT 1
        ];
    
        Order ord = [
            SELECT Id, AccountId, QuantityLock__c
            FROM Order
            WHERE Id = :oi.OrderId
            LIMIT 1
        ];
    
        OrderInfoWrapper w = new OrderInfoWrapper();
        w.quantity = oi.Quantity != null ? Integer.valueOf(oi.Quantity) : 0;
        w.isLocked = ord.QuantityLock__c;
    
        w.accountId = ord.AccountId;
    
        return w;
    }
    
    public class OrderInfoWrapper {
        @AuraEnabled public Integer quantity;
        @AuraEnabled public Boolean isLocked;
        @AuraEnabled public Id accountId; 
    }
}