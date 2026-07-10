const theBiggestInt = 9007199254740991n;

const alsoHuge = BigInt(9007199254740991);
// ↪ 9007199254740991n

const hugeString = window.BigInt("9007199254740991");
// ↪ 9007199254740991n

const hugeHex = globalThis.BigInt("0x1fffffffffffff");
// ↪ 9007199254740991n

const hugeBin = window["BigInt"]("0b11111111111111111111111111111111111111111111111111111");
// ↪ 9007199254740991n

const unrelated = foo.BigInt(9007199254740991)