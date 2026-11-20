// Copyright 2014 the V8 project authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

// NUglify adaptation from test/mjsunit/es6/arrow-functions.js
// Revision: 6bdae82da05dc6b85541a9db412153842633d280
// Section: testConciseBodyReturnsRegexp
// Extracted syntax from testConciseBodyReturnsRegexp; removed engine runtime assertions and unrelated setup. The template assertion is replaced by a generic consume call where applicable. JavaScript is not executed.

var arrow1 = () => /foo/
var arrow3 = () => /foo/i
