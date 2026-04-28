public with sharing class ProductController {

    @AuraEnabled(cacheable=true)
    public static List<Product2> getProducts() {
        return [SELECT Id, Name FROM Product2 LIMIT 50];
    }

}