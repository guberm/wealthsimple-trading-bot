import logging
from apscheduler.schedulers.blocking import BlockingScheduler
from apscheduler.triggers.cron import CronTrigger
import pytz

logger = logging.getLogger(__name__)


class TradingScheduler:
    """Schedules bot execution at multiple times during market hours using APScheduler."""

    def __init__(self, config, trading_callback):
        self._config = config
        self._callback = trading_callback
        self._scheduler = BlockingScheduler()

    def start(self) -> None:
        schedule_cfg = self._config.schedule
        tz_str = schedule_cfg.get("timezone", "America/Toronto")
        tz = pytz.timezone(tz_str)
        days = schedule_cfg.get("days", ["mon", "tue", "wed", "thu", "fri"])
        day_of_week = ",".join(days)

        run_times = schedule_cfg.get("run_times", ["09:35", "11:30", "13:30", "15:30"])

        for run_time in run_times:
            hour, minute = run_time.split(":")
            trigger = CronTrigger(
                hour=int(hour),
                minute=int(minute),
                day_of_week=day_of_week,
                timezone=tz,
            )
            self._scheduler.add_job(
                self._callback,
                trigger=trigger,
                id=f"trading_job_{run_time}",
                name=f"Trading Bot Run ({run_time} ET)",
                misfire_grace_time=300,
            )
            logger.info("Scheduled trading run at %s ET on %s", run_time, day_of_week)

        logger.info(
            "Scheduler starting with %d daily runs. Press Ctrl+C to stop.",
            len(run_times),
        )
        try:
            self._scheduler.start()
        except (KeyboardInterrupt, SystemExit):
            logger.info("Scheduler stopped by user")

    def stop(self) -> None:
        if self._scheduler.running:
            self._scheduler.shutdown(wait=False)
            logger.info("Scheduler shut down")

    def run_once(self) -> None:
        logger.info("Running trading pipeline once (manual trigger)")
        self._callback()
