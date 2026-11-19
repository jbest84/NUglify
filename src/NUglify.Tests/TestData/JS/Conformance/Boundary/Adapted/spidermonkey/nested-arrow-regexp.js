/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

// NUglify adaptation from js/src/tests/non262/arrow-functions/arrow-returning-arrow-with-block-body-followed-by-regexp.js
// Revision: 889724fd195b636bc6685a867c2bc1d34573293e
// Section: function t
// Extracted syntax from function t; removed engine runtime assertions and unrelated setup. The template assertion is replaced by a generic consume call where applicable. JavaScript is not executed.

function t() {
  var x = y => z => {}
  /Q/;
  return 42;
}
