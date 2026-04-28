public class OrderSkuHandler {

    public static void updateOrderItemTotals(
        List<Order_SKU__c> newList,
        List<Order_SKU__c> oldList
    ){
        Set<Id> orderItemIds = new Set<Id>();

        if(newList != null){
            for(Order_SKU__c rec : newList){
                if(rec.Order_Product__c != null)
                    orderItemIds.add(rec.Order_Product__c);
            }
        }

        if(oldList != null){
            for(Order_SKU__c rec : oldList){
                if(rec.Order_Product__c != null)
                    orderItemIds.add(rec.Order_Product__c);
            }
        }

        if(orderItemIds.isEmpty()) return;

        Map<Id, Decimal> totalsMap = new Map<Id, Decimal>();

        for(AggregateResult ar : [
            SELECT Order_Product__c orderItemId,
                   SUM(Total__c) total
            FROM Order_SKU__c
            WHERE Order_Product__c IN :orderItemIds
            GROUP BY Order_Product__c
        ]){
            totalsMap.put(
                (Id)ar.get('orderItemId'),
                (Decimal)ar.get('total')
            );
        }

        List<OrderItem> updates = new List<OrderItem>();

        for(Id oiId : orderItemIds){

            Decimal total = totalsMap.containsKey(oiId) 
                ? totalsMap.get(oiId) 
                : 0;

            updates.add(new OrderItem(
                Id = oiId,
                UnitPrice = total,   // IMPORTANT FIX
                Quantity = 1
            ));
        }

        if(!updates.isEmpty()){
            update updates;
        }
    }
}