import LightningModal from 'lightning/modal';

export default class SkuMatrixModal extends LightningModal {
    productId;

    handleSave() {
        this.close({
            totalQty: 0
        });
    }

    handleCancel() {
        this.close();
    }
}