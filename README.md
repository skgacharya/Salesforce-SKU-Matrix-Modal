# Salesforce-SKU-Matrix-Modal
 Custom Lightning Web Component to replace standard product quantity entry  with dynamic SKU-based quantity calculation.


# Problem:
Standard Salesforce quantity entry does not support SKU-level logic.

# Solution:
Custom SKU Matrix modal calculates quantity dynamically.

# Result:
Reduced manual errors and improved order accuracy.

## Features

- Product Selection
- SKU Matrix Modal
- Quantity Calculation
- Apex Integration

## Folder Structure

force-app/main/default/lwc/
    skuMatrixModal
    skuMatrix
    productSelector

## Deployment

Use Salesforce DX:

sfdx force:source:deploy -p force-app

## sku-matrix-salesforce-lwc/
├── force-app/
│   └── main/default/
│
│       ├── lwc/
│       │   ├── skuMatrixModal/
│       │   │   ├── skuMatrixModal.html
│       │   │   └── skuMatrixModal.js
│       │
│       │   ├── skuMatrix/
│       │   │   ├── skuMatrix.html
│       │   │   └── skuMatrix.js
│       │
│       │   ├── productSelector/
│       │       ├── productSelector.html
│       │       └── productSelector.js
│
│       ├── classes/
│       │   ├── ProductController.cls
│
├── screenshots/
│
├── README.md
