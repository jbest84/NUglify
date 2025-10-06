class MyClass
{
    constructor(fld)
    {
        this.fld = fld;
        this.date = new Date();
    }
}

class MyExtendedClass extends MyClass
{
    constructor(field)
    {
        super(field);
    }
}

function globalFunction()
{
    return true;
}

let myVar = new MyClass();
let myExtendedVar = new MyExtendedClass();
globalFunction();
