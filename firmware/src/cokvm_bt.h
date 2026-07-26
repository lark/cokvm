#ifndef COKVM_BT_H
#define COKVM_BT_H

#include <stdbool.h>
#include <stdint.h>
#include "cokvm_protocol.h"

int ck_bt_init(void);
void ck_bt_start_pairing_window(void);
bool ck_bt_is_connected(void);
void ck_bt_send_data(uint8_t led_status);

#if IS_ENABLED(CONFIG_COKVM_TESTSUITE)
int ck_bt_inject_test_data(const cokvm_vss_payload_t *data);
#endif

#endif
