import { DEFAULT_DAILY_BUDGET_USD } from "../config.js";
import { BudgetExceededError } from "../utils/errors.js";

export class BudgetGuard {
  constructor({ dailyBudgetUsd = DEFAULT_DAILY_BUDGET_USD } = {}) {
    this.dailyBudgetUsd = dailyBudgetUsd;
    this.spentUsd = 0;
  }

  charge(amountUsd) {
    if (this.spentUsd + amountUsd > this.dailyBudgetUsd) {
      throw new BudgetExceededError("Daily translation budget exceeded", {
        remainingUsd: this.dailyBudgetUsd - this.spentUsd
      });
    }
    this.spentUsd += amountUsd;
    return this.spentUsd;
  }

  reset() {
    this.spentUsd = 0;
  }
}

export default {
  BudgetGuard
};
