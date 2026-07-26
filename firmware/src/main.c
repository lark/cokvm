#include <zephyr/kernel.h>
#include <zephyr/logging/log.h>

BUILD_ASSERT(IS_ENABLED(CONFIG_SETTINGS),
             "CONFIG_SETTINGS must be enabled; otherwise CoKVM cannot save paired hosts.");
BUILD_ASSERT(IS_ENABLED(CONFIG_BT_SETTINGS),
             "CONFIG_BT_SETTINGS must be enabled to link BT bonds with NVS storage.");
BUILD_ASSERT(!IS_ENABLED(CONFIG_BT_PRIVACY),
             "CONFIG_BT_PRIVACY must NOT be enabled for this application.");
BUILD_ASSERT(CONFIG_BT_ID_MAX == 1,
             "CONFIG_BT_ID_MAX must be 1.");

#include "cokvm_protocol.h"
#include "cokvm_ui.h"
#include "cokvm_queue.h"
#include "cokvm_usb.h"
#include "cokvm_bt.h"

LOG_MODULE_REGISTER(cokvm_main, LOG_LEVEL_INF);

int main(void)
{
    int ret;
    LOG_INF("main() thread started.");

    ck_ui_init();

    ret = ck_usb_init();
    if (ret) {
        ck_ui_set_status(COKVM_UI_STATUS_ERROR);
        LOG_ERR("USB Composite init failed: %d", ret);
    } else {
        LOG_INF("USB Composite initialized.");
    }

    ret = ck_bt_init();
    if (ret) {
        ck_ui_set_status(COKVM_UI_STATUS_ERROR);
        LOG_ERR("BLE init failed: %d", ret);
    } else {
        LOG_INF("BLE initialized.");
    }

    ck_queue_info();

    ck_start_tx_work_threads();
    LOG_INF("TX work threads started.");

    while (1) {
        ck_ui_loop();
    }

    return 0;
}