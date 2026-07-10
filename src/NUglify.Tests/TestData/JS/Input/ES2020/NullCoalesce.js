let p = null;
let a = b ?? 'test';
let r = p ? p ?? true : false;
let s = null ?? 3;
let x = false ?? "string";

function y() {
    return 1 ?? x;
}
let z = y() + s;