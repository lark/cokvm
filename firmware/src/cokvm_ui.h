#ifndef COKVM_UI_H
#define COKVM_UI_H

#include <zephyr/kernel.h>

enum cokvm_ui_status {
    COKVM_UI_STATUS_NORMAL,
    COKVM_UI_STATUS_PAIRING,
    COKVM_UI_STATUS_CONNECTED,
    COKVM_UI_STATUS_ERROR
};

void ck_ui_init(void);
void ck_ui_set_status(enum cokvm_ui_status status);
enum cokvm_ui_status ck_ui_get_status(void);
void ck_ui_loop(void);

#endif
