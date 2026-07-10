const test = "1";
function test(module) {
    module.exports = function () {

    };
    module.foo = x(module);

    function x(module) {
        module.bar = "baz";
    }
}