/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

// NUglify adaptation from js/src/tests/non262/arrow-functions/arrow-not-as-end-of-statement.js
// Revision: 889724fd195b636bc6685a867c2bc1d34573293e
// Section: assertEq(`x ${a => {}} z`
// Extracted syntax from assertEq(`x ${a => {}} z`; removed engine runtime assertions and unrelated setup. The template assertion is replaced by a generic consume call where applicable. JavaScript is not executed.

consume(`x ${a => {}} z`);
