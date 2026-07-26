#include <zephyr/kernel.h>
#include <zephyr/usb/class/usbd_hid.h>
#include <zephyr/logging/log.h>
#include <zephyr/sys/sflist.h>

#include "cokvm_queue.h"
#include "cokvm_bt.h"
#include "cokvm_usb.h"

LOG_MODULE_REGISTER(ck_queue, LOG_LEVEL_INF);

#define COKVM_QUEUE_SLAB_BLOCKS 64

static K_FIFO_DEFINE(kbd_txqueue);
static K_FIFO_DEFINE(mouse_txqueue);

K_MEM_SLAB_DEFINE(queue_slab, ROUND_UP(sizeof(cokvm_txqueue_item_t), 4), COKVM_QUEUE_SLAB_BLOCKS, 4);

static void kbd_tx_worker(void *p1, void *p2, void *p3);
static void mouse_tx_worker(void *p1, void *p2, void *p3);

static struct k_thread kbd_tx_worker_thread_data;
static K_THREAD_STACK_DEFINE(kbd_tx_worker_stack, 512);

static struct k_thread mouse_tx_worker_thread_data;
static K_THREAD_STACK_DEFINE(mouse_tx_worker_stack, 512);

void ck_queue_info(void)
{
    LOG_INF("queue initialized: blocks=%d, item_size=%zu",
            COKVM_QUEUE_SLAB_BLOCKS, sizeof(cokvm_txqueue_item_t));
}

void ck_queue_status(struct ck_queue_status *status)
{
    if (!status) return;

    status->total_blocks = COKVM_QUEUE_SLAB_BLOCKS;
    status->free_blocks = k_mem_slab_num_free_get(&queue_slab);
    status->used_blocks = status->total_blocks - status->free_blocks;
    status->slab_start = (uintptr_t)queue_slab.buffer;
    status->slab_end = (uintptr_t)queue_slab.buffer + queue_slab.info.block_size * queue_slab.info.num_blocks;
    status->block_size = queue_slab.info.block_size;
}

void ck_free_queue_item(cokvm_txqueue_item_t *item)
{
    if (item) {
        k_mem_slab_free(&queue_slab, (void *)item);
    }
}

void ck_free_queue_item_from_report(const cokvm_usb_payload_t *report)
{
    if (!report) return;
    cokvm_txqueue_item_t *item = CONTAINER_OF(report, cokvm_txqueue_item_t, data.usb);
    k_mem_slab_free(&queue_slab, (void *)item);
}

cokvm_txqueue_item_t *ck_queue_peek_tx(void)
{
    return (cokvm_txqueue_item_t *)k_fifo_peek_head(&kbd_txqueue);
}

/* helper function to allocate queue item, memset to 0 */
cokvm_txqueue_item_t *ck_alloc_queue_item(void)
{
    cokvm_txqueue_item_t *item;
    int ret;

    ret = k_mem_slab_alloc(&queue_slab, (void **)&item, K_NO_WAIT);
    if (ret == 0) {
        memset(item, 0, sizeof(cokvm_txqueue_item_t));
        return item;
    } else {
        LOG_WRN("queue item slab alloc failed: %d", ret);
        return NULL;
    }
}

/* extract info from BLE VSS payload and put corresponding USB payload into usb_tx queue */
int ck_usb_tx_submit_from_vss_payload(const cokvm_vss_payload_t *payload)
{
    cokvm_txqueue_item_t *item;

    if (!payload) {
        return -EINVAL;
    }

    item = ck_alloc_queue_item();
    if (!item) {
        return -ENOMEM;
    }

    /*
     * caller must ensure payload type is correct before calling this function.
     * only keyboard and mouse data are used.
     */
    if (payload->type == COKVM_MSG_TYPE_KBD_DATA) {
        item->data.usb.report_id = COKVM_MSG_KBD_REPORT_ID;
        memcpy(&item->data.usb.data.kbd, &payload->data.kbd, sizeof(cokvm_kbd_data_t));
        k_fifo_put(&kbd_txqueue, item);
    } else if (payload->type == COKVM_MSG_TYPE_MOUSE_DATA) {
        item->data.usb.report_id = COKVM_MSG_MOUSE_REPORT_ID;
        memcpy(&item->data.usb.data.mouse, &payload->data.mouse, sizeof(cokvm_mouse_data_t));
        k_fifo_put(&mouse_txqueue, item);
    } else {
        k_mem_slab_free(&queue_slab, (void *)item);
        return -EINVAL;
    }

    return 0;
}

int ck_usb_tx_submit(const cokvm_usb_payload_t *payload)
{
    cokvm_txqueue_item_t *item;

    if (!payload) {
        return -EINVAL;
    }

    item = ck_alloc_queue_item();
    if (!item) {
        return -ENOMEM;
    }

    memcpy(&item->data.usb, payload, sizeof(cokvm_usb_payload_t));

    if (payload->report_id == COKVM_MSG_KBD_REPORT_ID) {
        k_fifo_put(&kbd_txqueue, item);
    } else {
        k_fifo_put(&mouse_txqueue, item);
    }

    return 0;
}

int ck_usb_tx_submit_mouse(const cokvm_usb_payload_t *payload)
{
    cokvm_txqueue_item_t *item;

    if (!payload) {
        return -EINVAL;
    }

    item = ck_alloc_queue_item();
    if (!item) {
        return -ENOMEM;
    }

    memcpy(&item->data.usb, payload, sizeof(cokvm_usb_payload_t));
    k_fifo_put(&mouse_txqueue, item);
    return 0;
}

int ck_usb_tx_submit_led(uint8_t toggle_mask)
{
    cokvm_usb_payload_t payload1 = { .report_id = COKVM_MSG_KBD_REPORT_ID };
    cokvm_usb_payload_t payload2 = { .report_id = COKVM_MSG_KBD_REPORT_ID };
    uint8_t mask = toggle_mask & COKVM_KBD_LED_MASK;

    int i = 0;
    if (mask & HID_KBD_LED_NUM_LOCK) {
        payload1.data.kbd.keycodes[i++] = HID_KEY_NUMLOCK;
    }
    if (mask & HID_KBD_LED_CAPS_LOCK) {
        payload1.data.kbd.keycodes[i++] = HID_KEY_CAPSLOCK;
    }
    if (i == 0) {
        return 0;
    }
    payload2.data.kbd.delay = 1;
    ck_usb_tx_submit(&payload1);
    ck_usb_tx_submit(&payload2);

    return 0;
}

static void kbd_tx_worker(void *p1, void *p2, void *p3)
{
    ARG_UNUSED(p1);
    ARG_UNUSED(p2);
    ARG_UNUSED(p3);

    LOG_INF("kbd_tx worker thread started");

    while (1) {
        cokvm_txqueue_item_t *item = k_fifo_get(&kbd_txqueue, K_FOREVER);
        if (!item) {
            continue;
        }

        uint8_t delay_units = item->data.usb.data.kbd.delay;
        if (delay_units > 0) {
            k_sleep(K_MSEC(delay_units * COKVM_KBD_DELAY_UNIT_MS));
        }

        if (!kbd_ready) {
            k_mem_slab_free(&queue_slab, (void *)item);
            continue;
        }

        item->data.usb.data.kbd.delay = 0;
        int ret = ck_usb_send(&item->data.usb);
        if (ret) {
            k_mem_slab_free(&queue_slab, (void *)item);
        }
    }
}

static void mouse_tx_worker(void *p1, void *p2, void *p3)
{
    ARG_UNUSED(p1);
    ARG_UNUSED(p2);
    ARG_UNUSED(p3);

    LOG_INF("mouse_tx worker thread started");

    while (1) {
        cokvm_txqueue_item_t *item = k_fifo_get(&mouse_txqueue, K_FOREVER);
        if (!item) {
            continue;
        }

        if (!mouse_ready || ck_usb_mouse_throttled()) {
            k_mem_slab_free(&queue_slab, (void *)item);
            continue;
        }

        int ret = ck_usb_send(&item->data.usb);
        if (ret) {
            k_mem_slab_free(&queue_slab, (void *)item);
        }
    }
}

void ck_start_tx_work_threads(void)
{
    k_thread_create(&kbd_tx_worker_thread_data, kbd_tx_worker_stack,
                    K_THREAD_STACK_SIZEOF(kbd_tx_worker_stack),
                    kbd_tx_worker, NULL, NULL, NULL,
                    K_PRIO_COOP(7), 0, K_NO_WAIT);

    k_thread_create(&mouse_tx_worker_thread_data, mouse_tx_worker_stack,
                    K_THREAD_STACK_SIZEOF(mouse_tx_worker_stack),
                    mouse_tx_worker, NULL, NULL, NULL,
                    K_PRIO_COOP(7), 0, K_NO_WAIT);
}
