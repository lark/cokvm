#ifndef COKVM_QUEUE_H
#define COKVM_QUEUE_H

#include <stdint.h>

#include "cokvm_protocol.h"

typedef enum {
    CK_QUEUE_MSG_KBD_DATA,
    CK_QUEUE_MSG_MOUSE_DATA,
    CK_QUEUE_MSG_LED_STATUS,
} cokvm_queue_msg_type_t;

typedef struct {
    void *fifo_reserved;
    union {
        cokvm_usb_payload_t usb;    /* for send to USB */
        cokvm_vss_payload_t vss;    /* for send to Bluetooth */
    } data;
} cokvm_txqueue_item_t;

void ck_queue_info(void);
cokvm_txqueue_item_t *ck_alloc_queue_item();
int ck_usb_tx_submit_from_vss_payload(const cokvm_vss_payload_t *payload);
int ck_usb_tx_submit(const cokvm_usb_payload_t *payload);
int ck_usb_tx_submit_mouse(const cokvm_usb_payload_t *payload);
int ck_usb_tx_submit_led(uint8_t toggle_mask);
void ck_start_tx_work_threads(void);

struct ck_queue_status {
    uint32_t total_blocks;
    uint32_t free_blocks;
    uint32_t used_blocks;
    uintptr_t slab_start;
    uintptr_t slab_end;
    size_t block_size;
};

void ck_queue_status(struct ck_queue_status *status);
void ck_free_queue_item(cokvm_txqueue_item_t *item);
void ck_free_queue_item_from_report(const cokvm_usb_payload_t *report);
cokvm_txqueue_item_t *ck_queue_peek_tx(void);

#endif
