// Copyright 2018 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

const cardValidator = require('simple-card-validator');
const { v4: uuidv4 } = require('uuid');
const pino = require('pino');
const otelApi = require('@opentelemetry/api'); // M3.1

// M4.4: payments_total{card_type,result}. Bounded labels only.
const meter = otelApi.metrics.getMeter('paymentservice');
const paymentsTotal = meter.createCounter('payments_total', {
  description: 'Charge attempts by card_type and result.',
});

function recordPayment(cardType, result) {
  paymentsTotal.add(1, {
    card_type: cardType,
    result: result,
  });
}

const logger = pino({
  name: 'paymentservice-charge',
  messageKey: 'message',
  formatters: {
    level (logLevelString, logLevelNum) {
      return { severity: logLevelString }
    }
  }
});

// M3.1: helper to mark the active span with an error + bounded attrs.
function recordChargeError(span, err, kind) {
  if (!span) return;
  span.recordException(err);
  span.setStatus({ code: otelApi.SpanStatusCode.ERROR, message: kind });
  span.setAttribute('app.payment.card_type', kind);
}


class CreditCardError extends Error {
  constructor (message) {
    super(message);
    this.code = 400; // Invalid argument error
  }
}

class InvalidCreditCard extends CreditCardError {
  constructor (cardType) {
    super(`Credit card info is invalid`);
  }
}

class UnacceptedCreditCard extends CreditCardError {
  constructor (cardType) {
    super(`Sorry, we cannot process ${cardType} credit cards. Only VISA or MasterCard is accepted.`);
  }
}

class ExpiredCreditCard extends CreditCardError {
  constructor (number, month, year) {
    super(`Your credit card (ending ${number.substr(-4)}) expired on ${month}/${year}`);
  }
}

/**
 * Verifies the credit card number and (pretend) charges the card.
 *
 * @param {*} request
 * @return transaction_id - a random uuid.
 */
module.exports = function charge (request) {
  const span = otelApi.trace.getActiveSpan(); // M3.1
  const { amount, credit_card: creditCard } = request;
  const cardNumber = creditCard.credit_card_number;
  const cardInfo = cardValidator(cardNumber);
  const {
    card_type: cardType,
    valid
  } = cardInfo.getCardDetails();

  if (!valid) {
    const err = new InvalidCreditCard();
    recordChargeError(span, err, 'invalid');
    recordPayment('other', 'invalid'); // M4.4
    throw err;
  }

  // Only VISA and mastercard is accepted, other card types (AMEX, dinersclub) will
  // throw UnacceptedCreditCard error.
  if (!(cardType === 'visa' || cardType === 'mastercard')) {
    const err = new UnacceptedCreditCard(cardType);
    recordChargeError(span, err, 'unsupported');
    recordPayment('other', 'unsupported'); // M4.4
    throw err;
  }

  // Also validate expiration is > today.
  const currentMonth = new Date().getMonth() + 1;
  const currentYear = new Date().getFullYear();
  const { credit_card_expiration_year: year, credit_card_expiration_month: month } = creditCard;
  if ((currentYear * 12 + currentMonth) > (year * 12 + month)) {
    const err = new ExpiredCreditCard(cardNumber.replace('-', ''), month, year);
    recordChargeError(span, err, 'expired');
    recordPayment(cardType, 'expired'); // M4.4
    throw err;
  }

  // M3.2: bounded card-type attribute on the success path too.
  if (span) {
    span.setAttribute('app.payment.card_type', cardType);
    span.setAttribute('app.payment.result', 'success');
  }

  // M4.4 + M2.3 business event.
  recordPayment(cardType, 'success');
  logger.info({
    event: 'payment_charged',
    card_type: cardType,
    currency_code: amount.currency_code,
    units: amount.units,
  }, 'payment_charged');
  logger.info(`Transaction processed: ${cardType} ending ${cardNumber.substr(-4)} \
    Amount: ${amount.currency_code}${amount.units}.${amount.nanos}`);

  return { transaction_id: uuidv4() };
};
