define("SomeModule", [], function() {
    return {
        someProperty: [{
            property1: "SomeString",
            property2: async () => {
                if (someVariable) {
                    return;
                } else {
                    let service1 = new SomeService();
                    service1.someMethod();
                    const const1 = await someFunction1();
                    const const2 = await someFunction2();
                }
            }
        }]
    };
});
