import { LightningElement, api, track } from 'lwc';
import bindProductSKU from '@salesforce/apex/SkuPickerController.bindProductSKU';
import getOrderSKU from '@salesforce/apex/SkuPickerController.getOrderSKU';
import saveMatrix from '@salesforce/apex/SkuPickerController.saveMatrix';
import getOrderQuantity from '@salesforce/apex/SkuPickerController.getOrderQuantity';
import { ShowToastEvent } from 'lightning/platformShowToastEvent';
import isQuantityLocked from '@salesforce/apex/SkuPickerController.isQuantityLocked';
import getOrderInfo from '@salesforce/apex/SkuPickerController.getOrderInfo';
import getSKUExtraData from '@salesforce/apex/SkuPickerController.getSKUExtraData';


export default class SkuMatrix extends LightningElement {

    @api recordId;

    @track matrix = [];
    lengths = [];
    hasSkus = false;

    // Controls UI enabling and "data loading" state
    isLoaded = false;

    @track currentTotal = 0;
    @track orderQty = 0;

    // Track invalid cells (non-integer entries); use immutable updates for reactivity
    @track invalidCells = new Set();

    isQuantityLocked = false;

    // Extra SKU metadata by Id (inHand, backOrder, proposed)
    extraDataMap = {};

    orderSkuMap = {};
    editedMap = {};

    connectedCallback() {
        this.loadData();
    }

    // =========================
    // LOAD DATA
    // =========================
    async loadData() {
        this.isLoaded = false;
        try {

            const masterData = (await bindProductSKU({ orderItemId: this.recordId })) || [];
            const orderData = (await getOrderSKU({ orderItemId: this.recordId })) || [];
            const lockFlag = await isQuantityLocked({ orderItemId: this.recordId });
            const orderQty = await getOrderQuantity({ orderItemId: this.recordId });

            const orderInfo = await getOrderInfo({ orderItemId: this.recordId });
            const extraData = await getSKUExtraData({ accountId: orderInfo.accountId });

            this.hasSkus = masterData && masterData.length > 0;
            this.isQuantityLocked = lockFlag;
            this.orderQty = Number(orderQty) || 0;

            this.orderSkuMap = {};
            orderData.forEach(rec => {
                this.orderSkuMap[rec.UniqueKey__c] = Number(rec.Quantity__c);
            });

            this.extraDataMap = extraData || {};
            
            if (!extraData || Object.keys(extraData).length === 0) {
                this.showToast('Warning', 'Some SKU extra data could not be loaded', 'warning');
            }

            this.buildMatrix(masterData);
            this.currentTotal = this.getMatrixSum();

            this.isLoaded = true;
        } catch(error) {
            this.isLoaded = false;
            this.showToast('Error', error?.body?.message || 'Load failed', 'error');
        }
    }

    
    // =========================
    // BUILD MATRIX
    // =========================
   buildMatrix(data = []) {
        let diameterSet = new Set();
        let lengthSet = new Set();
        let skuMap = {};

        this.columns = ['InHand', 'BackOrder', 'Purposed', 'Order'];

        data.forEach(sku => {
            let dia = parseFloat(sku.Diameter__c)?.toFixed(2);
            let len = parseFloat(sku.Length__c)?.toString();

            if (dia && len) {
                diameterSet.add(dia);
                lengthSet.add(len);
                skuMap[`${dia}_${len}`] = sku;
            }
        });

        this.lengths = Array.from(lengthSet)
            .map(Number)
            .sort((a, b) => a - b)
            .map(String);

        let diameters = Array.from(diameterSet)
            .map(Number)
            .sort((a, b) => a - b)
            .map(d => d.toFixed(2));

        this.matrix = diameters.map(dia => ({
            diameter: dia,
            values: this.lengths.map(len => {

                const sku = skuMap[`${dia}_${len}`];
                const key = `${this.recordId}_${sku?.Id}`;

               let qty = this.editedMap[key];

                if (qty === undefined) { qty = this.orderSkuMap[key];}
                qty = (qty !== undefined && qty !== null) ? Number(qty) : 0;

                const extra = sku ? (this.extraDataMap?.[sku.Id] || {}) : {};

                return {
                    length: len,
                    skuId: sku ? sku.Id : null,
                    qty,

                    inHand: extra.inHand || 0,
                    backOrder: extra.backOrder || 0,
                    proposed: extra.proposed || 0,

                    keyIH: `${sku?.Id || len}_ih`,
                    keyBO: `${sku?.Id || len}_bo`,
                    keyPR: `${sku?.Id || len}_pr`,
                    keyQTY: `${sku?.Id || len}_qty`,

                    isDisabled: !sku || !sku?.IsActive__c,
                    cellClass: (!sku || !sku?.IsActive__c) ? 'no-sku-cell' : '',
                    tooltip: !sku
                            ? 'SKU not available for this combination'
                            : (!sku.IsActive__c ? 'SKU is inactive and not allowed for order' : null)
                };
            })
        }));
    }

    // =========================    
    // HANDLE CHANGE ✅ FIXED KEY
    // =========================
    handleChange(event) {

        if(this.isQuantityLocked){
            this.showToast('Error','Quantity is locked. Editing not allowed.','error');
            return;
        }

        const dia = event.target.dataset.dia;
        const len = event.target.dataset.len;

        const row = this.matrix.find(r => r.diameter === dia);
        const cell = row?.values.find(c => c.length === len);

        if(!cell || !cell.skuId){ return; }

        const skuId = cell.skuId;
        const key = `${this.recordId}_${skuId}`;

        let value = event.target.value;

        // Normalize numeric input: integer-only, clamp to 0, and allow null for empty
        let n = value === '' || value === null ? 0 : Number(value);
        if (n !== null) {
            if (Number.isNaN(n)) {
                n = null;
            } else {
                n = Math.max(0, Math.floor(n));
            }
        }
        const newQty = n;

        if(value !== '' && !Number.isInteger(Number(value))){
            const next = new Set(this.invalidCells);
            next.add(key);
            this.invalidCells = next;
        } else {
            const next = new Set(this.invalidCells);
            next.delete(key);
            this.invalidCells = next;
        }

        this.editedMap[key] = newQty;

        this.matrix = this.matrix.map(r => ({
            ...r,
            values: r.values.map(c => {
                if(r.diameter === dia && c.length === len){
                    return { ...c, qty: newQty };
                }
                return c;
            })
        }));

        this.currentTotal = this.getMatrixSum();
    }

    get counterClass() {
        return this.currentTotal === this.orderQty
            ? 'slds-text-color_success'
            : 'slds-text-color_error';
    }

    get headerColumns() {
    return this.lengths.map(len => {
        return this.columns.map(col => {
            return {
                key: `${len}_${col}`,
                label: col,
                length: len
            };
        });
    });
}

    // ARIA busy state for table/spinner
    get isLoadingAria() {
        return (!this.isLoaded).toString();
    }

    get isSaveDisabled() {
        return (
            !this.isLoaded ||
            this.isQuantityLocked ||
            this.invalidCells.size > 0 ||
            this.currentTotal !== this.orderQty
        );
    }

    // =========================
    // SUM MATRIX ✅ NEW
    // =========================
    getMatrixSum() {
        let total = 0;

        this.matrix.forEach(row => {
            row.values.forEach(cell => {
                const val = Number(cell.qty);
                if (!isNaN(val) && val !== null) {
                    total += val;
                }
            });
        });

        return total;
    }
    // =========================
    // SAVE WITH VALIDATION ✅ NEW
    // =========================
    async handleSave() {

        if(!this.isLoaded){
            this.showToast('Error','Data still loading. Please wait.','error');
            return;
        }

        if(this.isQuantityLocked){
            this.showToast('Error','Quantity is locked. Editing not allowed.','error');
            return;
        }

        if(this.currentTotal !== this.orderQty){
            this.showToast(
                'Error',
                `Matrix total (${this.currentTotal}) must match Order quantity (${this.orderQty})`,
                'error'
            );
            return;
        }
        if(this.invalidCells.size > 0){
            this.showToast('Error','Only whole numbers allowed','error');
            return;
        }
        // ✅ No server call needed anymore
        this.saveMatrixData();

    }

    // =========================
    // SAVE MATRIX
    // =========================
    saveMatrixData(){
        let payload = [];
        this.matrix.forEach(row => {
            row.values.forEach(cell => {
                if (cell.skuId && cell.qty > 0) {
                    payload.push({ skuId: cell.skuId, quantity: cell.qty });
                }
            });
        });
        if (!payload.length) {
            this.showToast('Error', 'Enter at least one quantity', 'error');
            return;
        }

        saveMatrix({
            orderItemId: this.recordId,
            dataJson: JSON.stringify(payload)
        })
        .then(async () => { 

            this.showToast('Success', 'Saved successfully', 'success');

            this.editedMap = {};
            this.invalidCells.clear();

            this.matrix = [];
            this.orderSkuMap = {};

            await this.loadData();
        })
        .catch(error => {
            console.error('RAW ERROR:', error);
            console.error('STRING:', JSON.stringify(error));

            let message = 'Unknown error';

            if (error?.body?.message) {
                message = error.body.message;
            } else if (error?.body?.pageErrors && error.body.pageErrors.length > 0) {
                message = error.body.pageErrors[0].message;
            } else if (error?.body?.fieldErrors) {
                message = JSON.stringify(error.body.fieldErrors);
            }

            this.showToast('Error', message, 'error');
        });
    }

    showToast(title, message, variant) {
        this.dispatchEvent(new ShowToastEvent({ title, message, variant }));
    }
}