(async function () {
    try {
        for await (let num of generator()) {
            console.log(num);
        }
    } catch (e) {
        console.log('caught', e);
    }
})();