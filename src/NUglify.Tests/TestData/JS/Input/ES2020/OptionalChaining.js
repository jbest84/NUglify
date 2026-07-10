let x = foo?.bar;
let y = foo?.();
let z = foo?.()?.[a];

// should be treated as a conditional expression
let p = foo?.3 : 0;

a(x + y + z);