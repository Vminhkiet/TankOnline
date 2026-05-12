#include <iostream>
#include <fmt/core.h>
#include <asio.hpp>

using asio::ip::udp;

class TankServerUDP
{
private:
	udp::socket socket_;
	udp::endpoint sender_endpoint_;
	char data_[1024];
public:
	TankServerUDP(asio::io_context& io, short port) : socket_(io, udp::endpoint(udp::v4(), port))
	{
		fmt::print("...Server-Tank (UDP) dang lang nghe port {}...\n", port);
	}
private:
	void do_recv()
	{
		socket_.async_receive_from(
			asio::buffer(data_),
			sender_endpoint_,
			[this](const std::error_code& ec, size_t size)
			{
				if (!ec && size > 0)
				{
					fmt::print("Nhan {} bytes tu IP: {}\n",
						size,
						sender_endpoint_.address().to_string());

					fmt::print("Noi dung: {}\n", std::string(data_, size));

					do_send("Da nhan!");
				}
				else
				{
					fmt::print("Message nhan khong duoc {}", sender_endpoint_.address().to_string());
				}
			}
		);
	}
	void do_send(const std::string& message)
	{
		auto message_ptr = std::make_shared<std::string>(message);
		socket_.async_send_to(
			asio::buffer(*message_ptr),
			sender_endpoint_,
			[this, message_ptr](const std::error_code& ec, size_t size)
			{
				if (!ec)
				{

					do_recv();
				}
			}
		);
	}
};

int main()
{
	try {
		asio::io_context io_context;

		TankServerUDP server(io_context, 12345);

		io_context.run();
	}
	catch (std::exception& e) {
		fmt::print(stderr, "Loi C++: {}\n", e.what());
	}
	return 0;
}