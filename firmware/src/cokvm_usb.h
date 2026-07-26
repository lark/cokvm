#ifndef COKVM_USB_H
#define COKVM_USB_H

#include "cokvm_protocol.h"
#include <zephyr/spinlock.h>

typedef enum {
    INITIALIZING,
    SYNCING,
    SYNCED,
} state_t;

extern struct k_spinlock led_sync_lock;
extern uint8_t kbd_led;
extern state_t kbd_led_syncstate;
extern uint8_t sync_retry_count;
extern bool kbd_ready;
extern bool mouse_ready;

#define MAX_RETRY 3

int ck_usb_init(void);
int ck_usb_send(const cokvm_usb_payload_t *payload);
bool ck_usb_mouse_throttled(void);

#endif
